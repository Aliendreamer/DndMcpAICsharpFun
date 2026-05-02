using System.Diagnostics.CodeAnalysis;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace DndMcpAICsharpFun.Features.Admin;

public static partial class BooksAdminEndpoints
{
    public static RouteGroupBuilder MapBooksAdmin(this RouteGroupBuilder group)
    {
        group.MapPost("/books/register", RegisterBook).DisableAntiforgery();
        group.MapGet("/books", GetAllBooks);
        group.MapGet("/books/{id:int}/extract", ExtractBook).DisableAntiforgery();
        group.MapGet("/books/{id:int}/extracted", GetExtracted);
        group.MapPost("/books/{id:int}/ingest-json", IngestJson).DisableAntiforgery();
        group.MapDelete("/books/{id:int}", DeleteBook);
        group.MapPost("/books/{id:int}/cancel-extract", CancelExtract).DisableAntiforgery();
        group.MapPost("/books/{id:int}/extract-page/{pageNumber:int}", ExtractPage).DisableAntiforgery();
        return group;
    }

    private static async Task<IResult> RegisterBook(
        HttpContext httpContext,
        IIngestionTracker tracker,
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

        string? sourceName = null, version = null, displayName = null, originalFileName = null, filePath = null;

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
                        case "sourceName": sourceName = value; break;
                        case "version": version = value; break;
                        case "displayName": displayName = value; break;
                    }
                }

                section = await reader.ReadNextSectionAsync(ct);
            }

            if (filePath is null || originalFileName is null)
                return Results.Problem("file is required.", statusCode: 400);
            if (string.IsNullOrEmpty(sourceName) || string.IsNullOrEmpty(displayName))
                return Results.Problem("sourceName and displayName are required.", statusCode: 400);
            if (!Enum.TryParse<DndVersion>(version, ignoreCase: true, out var parsedVersion))
                return Results.Problem(
                    $"Invalid version '{version}'. Valid values: {string.Join(", ", Enum.GetNames<DndVersion>())}",
                    statusCode: 400);

            var record = new IngestionRecord
            {
                FilePath = filePath,
                FileName = originalFileName,
                FileHash = string.Empty,
                SourceName = sourceName,
                Version = parsedVersion.ToString(),
                DisplayName = displayName,
                Status = IngestionStatus.Pending,
            };

            var created = await tracker.CreateAsync(record, ct);
            LogBookRegistered(logger, created.DisplayName, created.Id, originalFileName);
            filePath = null;
            return Results.Accepted($"/admin/books/{created.Id}", created);
        }
        finally
        {
            if (filePath is not null && File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private static async Task<IResult> GetAllBooks(IIngestionTracker tracker)
    {
        var records = await tracker.GetAllAsync();
        return Results.Ok(records);
    }

    private static async Task<IResult> ExtractBook(
        int id,
        IIngestionTracker tracker,
        IIngestionQueue queue,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        if (record.Status == IngestionStatus.Processing)
            return Results.Conflict("Book is currently processing. Wait before re-extracting.");

        queue.TryEnqueue(new IngestionWorkItem(IngestionWorkType.Extract, id));
        return Results.Accepted($"/admin/books/{id}");
    }

    private static async Task<IResult> GetExtracted(
        int id,
        IIngestionTracker tracker,
        IEntityJsonStore jsonStore,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        var files = jsonStore.ListPageFiles(id).ToList();
        return Results.Ok(new { BookId = id, FileCount = files.Count, Files = files });
    }

    private static async Task<IResult> IngestJson(
        int id,
        IIngestionTracker tracker,
        IIngestionQueue queue,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        if (record.Status == IngestionStatus.Processing)
            return Results.Conflict("Book is currently processing.");

        queue.TryEnqueue(new IngestionWorkItem(IngestionWorkType.IngestJson, id));
        return Results.Accepted($"/admin/books/{id}");
    }

    private static async Task<IResult> DeleteBook(
        int id,
        IIngestionOrchestrator orchestrator,
        CancellationToken ct)
    {
        var result = await orchestrator.DeleteBookAsync(id, ct);
        return result switch
        {
            DeleteBookResult.Deleted => Results.NoContent(),
            DeleteBookResult.NotFound => Results.NotFound(),
            DeleteBookResult.Conflict => Results.Conflict("Book is currently being ingested. Wait for ingestion to complete before deleting."),
            _ => Results.StatusCode(500)
        };
    }

    private static IResult CancelExtract(
        int id,
        IExtractionCancellationRegistry cancellationRegistry)
    {
        var cancelled = cancellationRegistry.Cancel(id);
        return cancelled ? Results.Ok() : Results.NotFound();
    }

    private static async Task<IResult> ExtractPage(
        int id,
        int pageNumber,
        IIngestionOrchestrator orchestrator,
        IPdfStructuredExtractor extractor,
        IIngestionTracker tracker,
        CancellationToken ct,
        [FromQuery] bool? save = null)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        using var document = UglyToad.PdfPig.PdfDocument.Open(record.FilePath);
        var totalPages = document.NumberOfPages;
        if (pageNumber < 1 || pageNumber > totalPages)
            return Results.Problem(
                $"Page {pageNumber} is out of range. Book has {totalPages} pages.",
                statusCode: 400);

        var pageData = await orchestrator.ExtractSinglePageAsync(id, pageNumber, save ?? false, ct);
        if (pageData is null)
            return Results.NotFound($"Book with id {id} not found");

        return Results.Ok(pageData);
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
}

[ExcludeFromCodeCoverage]
public sealed record RegisterBookRequest(
    string SourceName,
    string Version,
    string DisplayName);
