using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Tests.TestDoubles;

/// <summary>
/// Disabled-by-default <see cref="IReranker"/> stub used in retrieval tests.
/// When <see cref="Enabled"/> is false (default), <see cref="RerankAsync"/> is never called
/// by the production code.
/// </summary>
public sealed class StubReranker : IReranker
{
    public bool Enabled { get; init; } = false;

    public Task<float[]> RerankAsync(
        string query, IReadOnlyList<string> passages, CancellationToken ct)
        => Task.FromResult(Array.Empty<float>());
}