using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore;

namespace DndMcpAICsharpFun.Features.Admin;

/// <summary>
/// One-time backfill for the stable <c>source_key</c> migration: stamps <c>source_key</c>
/// onto every existing block in <c>dnd_blocks</c> keyed by its <c>source_book</c> display name,
/// using <see cref="BookCatalog"/> as the mapping. Safe to run repeatedly (idempotent) — each
/// call re-applies the same key to the same blocks and returns the same per-book counts.
/// </summary>
public sealed class RetrievalBackfillService(IVectorStoreService vectorStore)
{
    public async Task<IReadOnlyDictionary<string, long>> BackfillAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var book in BookCatalog.All)
        {
            var count = await vectorStore.SetSourceKeyForBookAsync(book.DisplayName, book.Key, ct);
            result[book.Key] = count;
        }

        return result;
    }
}
