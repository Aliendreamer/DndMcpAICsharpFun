using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Embedding;

public interface IEmbeddingIngestor
{
    Task IngestAsync(IList<ContentChunk> chunks, string fileHash, CancellationToken ct = default);
}
