using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore;

namespace DndMcpAICsharpFun.Features.Ingestion;

/// <summary>
/// Metadata-only reconcile for <c>IngestionRecords</c>: a past DB reset can wipe tracking rows
/// while the vector store (<c>dnd_blocks</c> in Qdrant) still holds the ingested blocks. For every
/// <see cref="BookCatalog"/> entry that has blocks in Qdrant but no matching <c>IngestionRecord</c>
/// (matched by <c>DisplayName</c>), this creates a best-effort tracking row from the block count
/// alone — no re-ingest, no re-embed. Existing records are never modified or deleted.
/// </summary>
public sealed class RegistryReconcileService(IIngestionTracker tracker, IVectorStoreService vectorStore)
{
    public async Task<IReadOnlyList<string>> ReconcileAsync(CancellationToken ct = default)
    {
        var existing = await tracker.GetAllAsync(limit: int.MaxValue, offset: 0, ct);
        var existingDisplayNames = existing
            .Select(r => r.DisplayName)
            .ToHashSet(StringComparer.Ordinal);

        var blockCounts = await vectorStore.GetSourceBookCountsAsync(ct);

        var created = new List<string>();
        foreach (var book in BookCatalog.All)
        {
            if (existingDisplayNames.Contains(book.DisplayName))
                continue;

            if (!blockCounts.TryGetValue(book.DisplayName, out var count) || count <= 0)
                continue;

            await tracker.CreateAsync(new IngestionRecord
            {
                FilePath = string.Empty,
                FileName = string.Empty,
                FileHash = string.Empty,
                Version = book.Version.ToString(),
                DisplayName = book.DisplayName,
                Status = IngestionStatus.EntitiesIngested,
                ChunkCount = (int)count,
                EntityCount = null,
                FivetoolsSourceKey = book.FivetoolsSourceKey,
            }, ct);

            created.Add(book.DisplayName);
        }

        return created;
    }
}
