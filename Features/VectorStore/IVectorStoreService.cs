using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Infrastructure.Search;

namespace DndMcpAICsharpFun.Features.VectorStore;

public interface IVectorStoreService
{
    Task UpsertBlocksAsync(
        IList<(BlockChunk Chunk, float[] Vector, SparseVector Sparse, string FileHash)> points,
        CancellationToken ct = default);

    Task DeleteBlocksByHashAsync(string fileHash, CancellationToken ct = default);

    /// <summary>Counts blocks per <see cref="DndMcpAICsharpFun.Features.Retrieval.BookCatalog"/> key
    /// (the <c>source_key</c> payload field). Every catalog key is present, even with a 0 count.</summary>
    Task<IReadOnlyDictionary<string, long>> GetSourceKeyCountsAsync(CancellationToken ct = default);

    /// <summary>Counts blocks per <see cref="DndMcpAICsharpFun.Features.Retrieval.BookCatalog"/> display name
    /// (the <c>source_book</c> payload field). Every catalog display name is present, even with a 0 count.</summary>
    Task<IReadOnlyDictionary<string, long>> GetSourceBookCountsAsync(CancellationToken ct = default);

    /// <summary>Sets <c>source_key</c> on every block whose <c>source_book</c> equals <paramref name="displayName"/>.
    /// Returns the post-update count of blocks matching that <c>source_book</c>.</summary>
    Task<long> SetSourceKeyForBookAsync(string displayName, string key, CancellationToken ct = default);
}