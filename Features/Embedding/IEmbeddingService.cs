namespace DndMcpAICsharpFun.Features.Embedding;

public interface IEmbeddingService
{
    Task<IList<float[]>> EmbedAsync(IList<string> texts, CancellationToken ct = default);
}
