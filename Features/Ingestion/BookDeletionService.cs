using Microsoft.Extensions.Options;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Ingestion;

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

        if (!string.IsNullOrEmpty(record.FileHash))
            await vectorStore.DeleteBlocksByHashAsync(record.FileHash, cancellationToken);

        if (!string.IsNullOrEmpty(record.FileHash))
            await entityStore.DeleteByFileHashAsync(record.FileHash, cancellationToken);

        var canonicalSlug = CanonicalSlugOf(record);
        var canonicalPath = Path.Combine(entityIngestionOptions.Value.CanonicalDirectory, canonicalSlug + ".json");
        // Only delete the canonical file when no OTHER book resolves to the same slug — otherwise a
        // slug collision would delete a different book's canonical file (COR-18).
        var allRecords = await tracker.GetAllAsync(int.MaxValue, 0, cancellationToken);
        var slugSharedByOther = allRecords is not null
            && allRecords.Any(r => r.Id != id && CanonicalSlugOf(r) == canonicalSlug);
        if (!slugSharedByOther && File.Exists(canonicalPath))
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

    private static string CanonicalSlugOf(IngestionRecord record) =>
        EntityIdSlug.BookSlug(record);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted book {DisplayName} (id={Id})")]
    private static partial void LogBookDeleted(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted canonical JSON for book {BookId}: {Path}")]
    private static partial void LogCanonicalDeleted(ILogger logger, int bookId, string path);
}
