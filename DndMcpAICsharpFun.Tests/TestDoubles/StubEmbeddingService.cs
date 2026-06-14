using DndMcpAICsharpFun.Features.Embedding;

namespace DndMcpAICsharpFun.Tests.TestDoubles;

/// <summary>
/// Deterministic <see cref="IEmbeddingService"/> stub that returns fixed-size zero vectors
/// (or optionally a custom factory) for any input.
/// </summary>
public sealed class StubEmbeddingService : IEmbeddingService
{
    private readonly int _dimensions;
    private readonly Func<int, float[]>? _factory;

    /// <param name="dimensions">Length of each embedding vector (default 1024).</param>
    /// <param name="factory">Optional factory to produce each vector given its 0-based index.</param>
    public StubEmbeddingService(int dimensions = 1024, Func<int, float[]>? factory = null)
    {
        _dimensions = dimensions;
        _factory = factory;
    }

    public Task<IList<float[]>> EmbedAsync(IList<string> texts, CancellationToken ct = default)
    {
        IList<float[]> result = Enumerable.Range(0, texts.Count)
            .Select(i => _factory is not null ? _factory(i) : new float[_dimensions])
            .ToList();
        return Task.FromResult(result);
    }
}
