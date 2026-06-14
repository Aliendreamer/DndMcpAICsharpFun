using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Tests.TestDoubles;

/// <summary>
/// Disabled-by-default <see cref="IReranker"/> stub used in retrieval tests.
/// When <see cref="Enabled"/> is false (default), <see cref="RerankAsync"/> is never called
/// by the production code and <see cref="SelectTopN"/> returns the first N candidates unchanged.
/// </summary>
public sealed class StubReranker : IReranker
{
    public bool Enabled { get; init; } = false;

    public Task<float[]> RerankAsync(
        string query, IReadOnlyList<string> passages, CancellationToken ct)
        => Task.FromResult(Array.Empty<float>());

    public IList<RetrievalResult> SelectTopN(
        IEnumerable<RetrievalResult> candidates, float[] scores, int topN)
        => candidates.Take(topN).ToList();
}
