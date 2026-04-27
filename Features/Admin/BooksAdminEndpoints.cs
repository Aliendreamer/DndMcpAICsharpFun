using System.Security.Cryptography;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

public static partial class BooksAdminEndpoints
{
    public static RouteGroupBuilder MapBooksAdmin(this RouteGroupBuilder group)
    {
        group.MapPost("/books/register", RegisterBook).DisableAntiforgery();
        group.MapGet("/books", GetAllBooks);
        group.MapPost("/books/{id:int}/reingest", ReingestBook);
        group.MapDelete("/books/{id:int}", DeleteBook);
        return group;
    }

    private static async Task<IResult> RegisterBook(
      IFormFile file,
      string sourceName,
      string version,
      string displayName,
      IIngestionTracker tracker,
      IIngestionOrchestrator orchestrator,
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

        _ = Task.Run(
            () => orchestrator.IngestBookAsync(created.Id, CancellationToken.None),
            CancellationToken.None);

        return Results.Accepted($"/admin/books/{created.Id}", created);
    }
    private static async Task<IResult> GetAllBooks(IIngestionTracker tracker)
    {
        var records = await tracker.GetAllAsync();
        return Results.Ok(records);
    }

    private static async Task<IResult> ReingestBook(
        int id,
        IIngestionTracker tracker,
        IIngestionOrchestrator orchestrator,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        await tracker.ResetForReingestionAsync(id, ct);
        _ = Task.Run(() => orchestrator.IngestBookAsync(id, CancellationToken.None), ct);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered book {DisplayName} (id={Id}, file={File})")]
    private static partial void LogBookRegistered(ILogger logger, string displayName, int id, string file);
}

public sealed record RegisterBookRequest(
    string SourceName,
    string Version,
    string DisplayName);
