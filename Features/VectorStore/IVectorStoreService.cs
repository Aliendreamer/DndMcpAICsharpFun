using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Infrastructure.Search;

namespace DndMcpAICsharpFun.Features.VectorStore;

public interface IVectorStoreService
{
    Task UpsertBlocksAsync(
        IList<(BlockChunk Chunk, float[] Vector, SparseVector Sparse, string FileHash)> points,
        CancellationToken ct = default);

    Task DeleteBlocksByHashAsync(string fileHash, CancellationToken ct = default);
}