using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

public static partial class BooksAdminEndpoints
{
    public static RouteGroupBuilder MapBooksAdmin(this RouteGroupBuilder group)
    {
        group.MapPost("/books/register", RegisterBook).DisableAntiforgery();
        group.MapPost("/books/register-path", RegisterBookByPath);
        group.MapGet("/books", GetAllBooks);
        group.MapPost("/books/{id:int}/extract", ExtractBook).DisableAntiforgery();
        group.MapGet("/books/{id:int}/extracted", GetExtracted);
        group.MapPost("/books/{id:int}/ingest-json", IngestJson).DisableAntiforgery();
        group.MapDelete("/books/{id:int}", DeleteBook);
        group.MapPost("/books/{id:int}/cancel-extract", CancelExtract).DisableAntiforgery();
        return group;
    }

    private static async Task<IResult> RegisterBook(
      IFormFile file,
      string sourceName,
      string version,
      string displayName,
      IIngestionTracker tracker,
      IOptions<IngestionOptions> ingestionOptions,
      ILogger<RegisterBookRequest> logger,
      CancellationToken ct)
    {
        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Results.Problem("Only PDF files are accepted.", statusCode: 400);

        if (!Enum.TryParse<DndVersion>(version, ignoreCase: true, out var parsedVersion))
            return Results.Problem(
                $"Invalid version '{version}'. Valid values: {string.Join(", ", Enum.GetNames<DndVersion>())}",
                statusCode: 400);

        var booksPath = ingestionOptions.Value.BooksPath;
        Directory.CreateDirectory(booksPath);

        var originalFileName = Path.GetFileName(file.FileName);
        var storedFileName = $"{Guid.NewGuid():N}_{originalFileName}";
        var filePath = Path.Combine(booksPath, storedFileName);

        await using (var src = file.OpenReadStream())
        await using (var dest = File.Create(filePath))
        {
            await src.CopyToAsync(dest, ct);
        }

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

        return Results.Accepted($"/admin/books/{created.Id}", created);
    }
    private static async Task<IResult> RegisterBookByPath(
        RegisterBookByPathRequest request,
        IIngestionTracker tracker,
        ILogger<RegisterBookByPathRequest> logger,
        CancellationToken ct)
    {
        if (!request.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Results.Problem("Only PDF files are accepted.", statusCode: 400);

        if (!File.Exists(request.FilePath))
            return Results.Problem($"File not found: {request.FilePath}", statusCode: 400);

        if (!Enum.TryParse<DndVersion>(request.Version, ignoreCase: true, out var parsedVersion))
            return Results.Problem(
                $"Invalid version '{request.Version}'. Valid values: {string.Join(", ", Enum.GetNames<DndVersion>())}",
                statusCode: 400);

        var record = new IngestionRecord
        {
            FilePath = request.FilePath,
            FileName = Path.GetFileName(request.FilePath),
            FileHash = string.Empty,
            SourceName = request.SourceName,
            Version = parsedVersion.ToString(),
            DisplayName = request.DisplayName,
            Status = IngestionStatus.Pending,
        };

        var created = await tracker.CreateAsync(record, ct);
        LogBookRegistered(logger, created.DisplayName, created.Id, created.FileName);

        return Results.Accepted($"/admin/books/{created.Id}", created);
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
            DeleteBookResult.Deleted   => Results.NoContent(),
            DeleteBookResult.NotFound  => Results.NotFound(),
            DeleteBookResult.Conflict  => Results.Conflict("Book is currently being ingested. Wait for ingestion to complete before deleting."),
            _                          => Results.StatusCode(500)
        };
    }

    private static IResult CancelExtract(
        int id,
        IExtractionCancellationRegistry cancellationRegistry)
    {
        var cancelled = cancellationRegistry.Cancel(id);
        return cancelled ? Results.Ok() : Results.NotFound();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered book {DisplayName} (id={Id}, file={File})")]
    private static partial void LogBookRegistered(ILogger logger, string displayName, int id, string file);
}

public sealed record RegisterBookRequest(
    string SourceName,
    string Version,
    string DisplayName);

public sealed record RegisterBookByPathRequest(
    string FilePath,
    string SourceName,
    string Version,
    string DisplayName);
