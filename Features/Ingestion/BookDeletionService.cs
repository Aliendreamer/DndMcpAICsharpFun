using Microsoft.Extensions.Options;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed partial class BookDeletionService(
    IIngestionTracker tracker,
    IVectorStoreService vectorStore,
    IEntityVectorStore entityStore,
    IOptions<EntityIngestionOptions> entityIngestionOptions,
    ILogger<BookDeletionService> logger) : IBookDeletionService
{
    public async Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken cancellationToken = default)
    {
        var record = await tracker.GetByIdAsync(id, cancellationToken);
        if (record is null)
            return DeleteBookResult.NotFound;

        if (record.Status == IngestionStatus.Processing)
            return DeleteBookResult.Conflict;

        if (record.Status == IngestionStatus.JsonIngested && record.ChunkCount.HasValue)
            await vectorStore.DeleteBlocksByHashAsync(record.FileHash, record.ChunkCount.Value, cancellationToken);

        await entityStore.DeleteByFileHashAsync(record.FileHash, cancellationToken);

        var canonicalSlug = EntityIdSlug.For(record.DisplayName, EntityType.Class, "x").Split('.')[0];
        var canonicalPath = Path.Combine(entityIngestionOptions.Value.CanonicalDirectory, canonicalSlug + ".json");
        if (File.Exists(canonicalPath))
        {
            File.Delete(canonicalPath);
            LogCanonicalDeleted(logger, id, canonicalPath);
        }

        if (File.Exists(record.FilePath))
            File.Delete(record.FilePath);

        await tracker.DeleteAsync(id, cancellationToken);
        LogBookDeleted(logger, record.DisplayName, id);
        return DeleteBookResult.Deleted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted book {DisplayName} (id={Id})")]
    private static partial void LogBookDeleted(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted canonical JSON for book {BookId}: {Path}")]
    private static partial void LogCanonicalDeleted(ILogger logger, int bookId, string path);
}
