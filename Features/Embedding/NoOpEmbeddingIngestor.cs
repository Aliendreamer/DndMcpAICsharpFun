using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Embedding;

public sealed class NoOpEmbeddingIngestor : IEmbeddingIngestor
{
    public Task IngestAsync(IList<ContentChunk> chunks, string fileHash, CancellationToken ct = default)
        => Task.CompletedTask;
}
