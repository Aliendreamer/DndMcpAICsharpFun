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
        group.MapPost("/books/register", RegisterBook);
        group.MapGet("/books", GetAllBooks);
        group.MapPost("/books/{id:int}/reingest", ReingestBook);
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

        var filePath = Path.Combine(booksPath, file.FileName);

        string hash;
        await using (var dest = File.Create(filePath))
        {
            await using var src = file.OpenReadStream();
            await src.CopyToAsync(dest, ct);
        }

        await using (var stream = File.OpenRead(filePath))
        {
            var hashBytes = await SHA256.HashDataAsync(stream, ct);
            hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var existing = await tracker.GetByHashAsync(hash, ct);
        if (existing is not null)
            return Results.Ok(existing);

        var record = new IngestionRecord
        {
            FilePath = filePath,
            FileName = file.FileName,
            FileHash = hash,
            SourceName = sourceName,
            Version = parsedVersion.ToString(),
            DisplayName = displayName,
            Status = IngestionStatus.Pending,
        };

        var created = await tracker.CreateAsync(record, ct);
        Log.BookRegistered(logger, created.DisplayName, created.Id, file.FileName);

        return Results.Created($"/admin/books/{created.Id}", created);
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

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Registered book {DisplayName} (id={Id}, file={File})")]
        public static partial void BookRegistered(ILogger logger, string displayName, int id, string file);
    }
}

public sealed record RegisterBookRequest(
    string SourceName,
    string Version,
    string DisplayName);
