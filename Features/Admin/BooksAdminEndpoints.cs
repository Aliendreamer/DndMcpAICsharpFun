using System.Diagnostics.CodeAnalysis;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Ingestion;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace DndMcpAICsharpFun.Features.Admin;

public static partial class BooksAdminEndpoints
{
    private sealed record RegisterBookResponse(
        IngestionRecord Record,
        IReadOnlyList<string> SuggestedSources);

    public static RouteGroupBuilder MapBooksAdmin(this RouteGroupBuilder group)
    {
        group.MapPost("/books/register", RegisterBook).DisableAntiforgery();
        group.MapGet("/books", GetAllBooks);
        group.MapDelete("/books/{id:int}", DeleteBook);
        group.MapPost("/books/{id:int}/ingest-blocks", IngestBlocks).DisableAntiforgery();
        group.MapPost("/books/{id:int}/ingest-entities", IngestEntities).DisableAntiforgery();
        group.MapPost("/books/{id:int}/extract-entities", ExtractEntities).DisableAntiforgery();
        return group;
    }

    private static async Task<IResult> IngestBlocks(
        int id,
        IIngestionTracker tracker,
        IIngestionQueue queue,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        if (record.Status is IngestionStatus.Processing
            or IngestionStatus.EntitiesExtracting
            or IngestionStatus.EntitiesIngesting)
            return Results.Conflict("Book is currently processing.");

        queue.TryEnqueue(new IngestionWorkItem(IngestionWorkType.IngestBlocks, id));
        return Results.Accepted($"/admin/books/{id}");
    }

    private static async Task<IResult> IngestEntities(
        int id,
        IIngestionTracker tracker,
        IIngestionQueue queue,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        if (record.Status is IngestionStatus.Processing
            or IngestionStatus.EntitiesExtracting
            or IngestionStatus.EntitiesIngesting)
            return Results.Conflict("Book is currently processing.");

        queue.TryEnqueue(new IngestionWorkItem(IngestionWorkType.IngestEntities, id));
        return Results.Accepted($"/admin/books/{id}");
    }

    private static async Task<IResult> ExtractEntities(
        int id,
        bool? force,
        bool? errorsOnly,
        IIngestionTracker tracker,
        IIngestionQueue queue,
        IOptions<EntityExtractionOptions> opts,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        var forceFlag = force ?? false;
        var errorsOnlyFlag = errorsOnly ?? false;

        // A stuck EntitiesExtracting/EntitiesIngesting status left by an interrupted run can be
        // overridden with ?force=true; an in-flight block ingestion (Processing) is never overridden.
        var stuckEntityStatus = record.Status is IngestionStatus.EntitiesExtracting
            or IngestionStatus.EntitiesIngesting;
        if (record.Status == IngestionStatus.Processing || (stuckEntityStatus && !forceFlag))
            return Results.Conflict("Book is currently processing.");

        if (forceFlag && errorsOnlyFlag)
            return Results.Problem(
                "?force and ?errorsOnly are mutually exclusive.",
                statusCode: StatusCodes.Status400BadRequest);

        if (errorsOnlyFlag)
        {
            var bookSlug = CanonicalSlugFor(record);
            var canonicalPath = Path.Combine(opts.Value.CanonicalDirectory, $"{bookSlug}.json");
            if (!File.Exists(canonicalPath))
                return Results.Conflict($"No canonical file found for {bookSlug}; run full extraction first.");
        }

        if (!errorsOnlyFlag)
        {
            var bookSlug = CanonicalSlugFor(record);
            var canonicalPath = Path.Combine(opts.Value.CanonicalDirectory, $"{bookSlug}.json");
            if (File.Exists(canonicalPath) && !forceFlag)
                return Results.Conflict($"Canonical file already exists at {canonicalPath}. Use ?force=true to overwrite.");
        }

        queue.TryEnqueue(new IngestionWorkItem(
            IngestionWorkType.ExtractEntities, id,
            Force: forceFlag, ErrorsOnly: errorsOnlyFlag));
        return Results.Accepted($"/admin/books/{id}");
    }

    private static async Task<IResult> RegisterBook(
        HttpContext httpContext,
        IIngestionTracker tracker,
        BookSourceRegistry registry,
        IOptions<IngestionOptions> ingestionOptions,
        ILogger<RegisterBookRequest> logger,
        CancellationToken ct)
    {
        var contentType = httpContext.Request.ContentType;
        if (string.IsNullOrEmpty(contentType) ||
            !contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            return Results.Problem("Expected multipart/form-data.", statusCode: 400);

        var boundary = HeaderUtilities.RemoveQuotes(
            MediaTypeHeaderValue.Parse(contentType).Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
            return Results.Problem("Missing multipart boundary.", statusCode: 400);

        var booksPath = ingestionOptions.Value.BooksPath;
        Directory.CreateDirectory(booksPath);

        string? version = null, displayName = null, originalFileName = null, filePath = null;
        string? bookTypeRaw = null;
        string? fivetoolsSourceKey = null;

        var reader = new MultipartReader(boundary, httpContext.Request.Body);
        var section = await reader.ReadNextSectionAsync(ct);
        try
        {
            while (section is not null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd))
                {
                    section = await reader.ReadNextSectionAsync(ct);
                    continue;
                }

                if (cd.FileName.HasValue || cd.FileNameStar.HasValue)
                {
                    var rawName = (cd.FileNameStar.HasValue ? cd.FileNameStar.Value : cd.FileName.Value) ?? string.Empty;
                    originalFileName = SanitizeDisplayFileName(rawName);
                    if (!originalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        return Results.Problem("Only PDF files are accepted.", statusCode: 400);

                    filePath = Path.Combine(booksPath, $"{Guid.NewGuid():N}.pdf");
                    await using var dest = File.Create(filePath);
                    await section.Body.CopyToAsync(dest, ct);
                }
                else if (cd.Name.HasValue)
                {
                    using var sr = new StreamReader(section.Body);
                    var value = await sr.ReadToEndAsync(ct);
                    switch (cd.Name.Value)
                    {
                        case "version": version = value; break;
                        case "displayName": displayName = value; break;
                        case "bookType": bookTypeRaw = value; break;
                        case "fivetoolsSourceKey": fivetoolsSourceKey = value; break;
                    }
                }

                section = await reader.ReadNextSectionAsync(ct);
            }

            if (filePath is null || originalFileName is null)
                return Results.Problem("file is required.", statusCode: 400);
            if (string.IsNullOrEmpty(displayName))
                return Results.Problem("displayName is required.", statusCode: 400);
            if (!Enum.TryParse<DndVersion>(version, ignoreCase: true, out var parsedVersion))
                return Results.Problem(
                    $"Invalid version '{version}'. Valid values: {string.Join(", ", Enum.GetNames<DndVersion>())}",
                    statusCode: 400);

            var bookType = Enum.TryParse<BookType>(bookTypeRaw, ignoreCase: true, out var parsedType)
                ? parsedType
                : BookType.Unknown;

            if (fivetoolsSourceKey is not null && registry.TryGetBook(fivetoolsSourceKey) is null)
                return Results.UnprocessableEntity(
                    $"Unknown fivetoolsSourceKey '{fivetoolsSourceKey}'. Call GET /admin/5etools/sources for valid values.");

            var record = new IngestionRecord
            {
                FilePath = filePath,
                FileName = originalFileName,
                FileHash = string.Empty,
                Version = parsedVersion.ToString(),
                DisplayName = displayName,
                Status = IngestionStatus.Pending,
                BookType = bookType,
                FivetoolsSourceKey = fivetoolsSourceKey,
            };

            var created = await tracker.CreateAsync(record, ct);
            LogBookRegistered(logger, created.DisplayName, created.Id, originalFileName);
            filePath = null;
            var suggestions = fivetoolsSourceKey is null
                ? registry.SuggestByName(displayName ?? "")
                : (IReadOnlyList<string>)Array.Empty<string>();
            return Results.Created($"/admin/books/{created.Id}", new RegisterBookResponse(created, suggestions));
        }
        finally
        {
            if (filePath is not null && File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private static async Task<IResult> GetAllBooks(
        IIngestionTracker tracker,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        var records = await tracker.GetAllAsync(limit, offset, ct);
        return Results.Ok(records);
    }

    

    

    

    private static async Task<IResult> DeleteBook(
        int id,
        IBookDeletionService deletionService,
        CancellationToken ct)
    {
        var result = await deletionService.DeleteBookAsync(id, ct);
        return result switch
        {
            DeleteBookResult.Deleted => Results.NoContent(),
            DeleteBookResult.NotFound => Results.NotFound(),
            DeleteBookResult.Conflict => Results.Conflict("Book is currently being ingested. Wait for ingestion to complete before deleting."),
            _ => Results.StatusCode(500)
        };
    }

    

    

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered book {DisplayName} (id={Id}, file={File})")]
    private static partial void LogBookRegistered(ILogger logger, string displayName, int id, string file);

    private static string SanitizeDisplayFileName(string raw)
    {
        var name = Path.GetFileName(raw.Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name
            .Where(c => !char.IsControl(c) && Array.IndexOf(invalid, c) < 0)
            .ToArray();
        var cleaned = new string(chars).Trim().Trim('.');
        if (cleaned.Length > 200) cleaned = cleaned[..200];
        return cleaned.Length == 0 ? "upload.pdf" : cleaned;
    }

    private static string CanonicalSlugFor(IngestionRecord record) =>
        record.FivetoolsSourceKey is { } key
            ? EntityIdSlug.For(key, EntityType.Class, "x").Split('.')[0]
            : EntityIdSlug.For(record.DisplayName, EntityType.Class, "x").Split('.')[0];
}

[ExcludeFromCodeCoverage]
public sealed record RegisterBookRequest(
    string SourceName,
    string Version,
    string DisplayName);
