namespace DndMcpAICsharpFun.Features.VectorStore;

public interface IVectorStoreService
{
    Task UpsertBlocksAsync(
        IList<(BlockChunk Chunk, float[] Vector, string FileHash)> points,
        CancellationToken ct = default);

    Task DeleteBlocksByHashAsync(string fileHash, int blockCount, CancellationToken ct = default);
}
