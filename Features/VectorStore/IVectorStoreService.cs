using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.VectorStore;

public interface IVectorStoreService
{
    Task UpsertAsync(
        IList<(ContentChunk Chunk, float[] Vector, string FileHash)> points,
        CancellationToken ct = default);

    Task DeleteByHashAsync(string fileHash, int chunkCount, CancellationToken ct = default);
}
