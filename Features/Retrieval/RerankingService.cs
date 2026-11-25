using DndMcpAICsharpFun.Features.Entities.CanonicalText;

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

    private const int MaxPiecesScored = 4;

    /// <summary>
    /// Reranks candidates whose text is longer than the cross-encoder reads: each is scored by its best piece. At most
    /// <see cref="MaxPiecesScored"/> pieces per candidate are scored: the first, and those holding the most query words.
    /// Without a reranker the original order stays and the piece with the most query words is the best one.
    /// </summary>
    public async Task<IReadOnlyList<(T Candidate, TextPiece Best)>> RerankByPieceAsync<T>(
        string query, IReadOnlyList<T> candidates, Func<T, string> getText, int finalTopN, CancellationToken ct)
    {
        var words = QueryWords(query);
        var ranked = !_opts.Enabled || !reranker.Enabled;
        var pool = (ranked ? candidates.Take(finalTopN) : candidates)
            .Select(c => (Candidate: c, Pieces: Likeliest(EntityTextPieces.Split(getText(c)), words)))
            .ToList();
        if (ranked || pool.Count == 0)
            return [.. pool.Select(p => (p.Candidate, p.Pieces[0]))];

        var passages = pool.SelectMany(p => p.Pieces).Select(p => p.Text).ToList();
        var scores = await reranker.RerankAsync(query, passages, ct);
        var result = new List<(T Candidate, TextPiece Best, float Score)>(pool.Count);
        var at = 0;
        foreach (var (candidate, pieces) in pool)
        {
            var best = Enumerable.Range(0, pieces.Count).MaxBy(i => scores[at + i]);
            result.Add((candidate, pieces[best], scores[at + best]));
            at += pieces.Count;
        }
        return [.. result.OrderByDescending(r => r.Score).Take(finalTopN).Select(r => (r.Candidate, r.Best))];
    }

    /// <summary>The pieces worth scoring, likeliest first; the opening (which names the entity) is always among them.</summary>
    private static IReadOnlyList<TextPiece> Likeliest(IReadOnlyList<TextPiece> pieces, HashSet<string> words)
    {
        if (pieces.Count == 1) return pieces;
        var top = pieces.Select((p, i) => (Piece: p, Index: i, Hits: words.Count(w => p.Text.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(x => x.Hits).ThenBy(x => x.Index)
            .Take(MaxPiecesScored).ToList();
        if (top.All(x => x.Index != 0)) top[^1] = (pieces[0], 0, 0);
        return [.. top.Select(x => x.Piece)];
    }

    private static HashSet<string> QueryWords(string query) =>
        query.Split([' ', ',', '.', '?', '!', ';', ':', '"', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3).ToHashSet(StringComparer.OrdinalIgnoreCase);
}