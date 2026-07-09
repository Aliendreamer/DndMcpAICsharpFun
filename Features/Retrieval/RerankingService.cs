using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Retrieval;

/// <summary>
/// Generic shared reranking service. Wraps <see cref="IReranker"/> so all retrieval paths
/// (prose, entity, fused) share one code path. When reranking is unavailable (globally
/// disabled, per-channel disabled, or model not loaded) returns the first N candidates
/// unchanged.
/// </summary>
public sealed class RerankingService(IReranker reranker, IOptions<RerankerOptions> options)
{
    private readonly RerankerOptions _opts = options.Value;

    /// <summary>
    /// Reranks <paramref name="candidates"/> for <paramref name="query"/> using
    /// <paramref name="getText"/> to extract passage text, then returns the top
    /// <paramref name="finalTopN"/> by cross-encoder score.
    /// When reranking is disabled the first <paramref name="finalTopN"/> candidates
    /// are returned in their original order without any model invocation.
    /// </summary>
    public async Task<IReadOnlyList<T>> RerankAsync<T>(
        string query,
        IReadOnlyList<T> candidates,
        Func<T, string> getText,
        int finalTopN,
        CancellationToken ct)
    {
        if (!_opts.Enabled || !reranker.Enabled || candidates.Count == 0)
            return candidates.Take(finalTopN).ToList();

        var passages = candidates.Select(getText).ToList();
        var scores = await reranker.RerankAsync(query, passages, ct);

        return candidates
            .Zip(scores, (c, s) => (Candidate: c, Score: s))
            .OrderByDescending(t => t.Score)
            .Take(finalTopN)
            .Select(t => t.Candidate)
            .ToList();
    }
}