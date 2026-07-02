using System.Text;
using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Infrastructure.Search;

public static class Bm25Vectorizer
{
    private const float K1 = 1.5f;
    private const float B = 0.75f;
    private const int VocabSize = 30000;

    // Deterministic term -> sparse index. String.GetHashCode() is randomized per process in .NET, so
    // sparse vectors written at ingestion time would not align with query-time vectors after a
    // restart. FNV-1a over the UTF-8 bytes is stable across processes (and never overflows Math.Abs).
    public static int StableIndex(string term)
    {
        const uint fnvOffsetBasis = 2166136261;
        const uint fnvPrime = 16777619;
        var hash = fnvOffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(term))
        {
            hash ^= b;
            hash *= fnvPrime;
        }
        return (int)(hash % VocabSize);
    }

    public static IReadOnlyList<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var lower = text.ToLowerInvariant();
        var start = -1;
        for (var i = 0; i <= lower.Length; i++)
        {
            var isTokenChar = i < lower.Length && char.IsLetterOrDigit(lower[i]);
            if (isTokenChar && start == -1)
            {
                start = i;
            }
            else if (!isTokenChar && start != -1)
            {
                tokens.Add(lower[start..i]);
                start = -1;
            }
        }
        return tokens;
    }

    // Doc vectors: BM25 tf-saturation × GLOBAL idf (idf lives here, applied ONCE).
    public static SparseVector[] ComputeDocVectors(IReadOnlyList<string> texts, Bm25GlobalStats stats)
    {
        var n = stats.DocumentCount;
        var avgDocLen = stats.AvgDocLength <= 0 ? 1f : (float)stats.AvgDocLength;
        var result = new SparseVector[texts.Count];
        for (var i = 0; i < texts.Count; i++)
        {
            var tokens = Tokenize(texts[i]);
            var docLen = tokens.Count;
            var termFreqs = new Dictionary<string, int>();
            foreach (var t in tokens) termFreqs[t] = termFreqs.TryGetValue(t, out var c) ? c + 1 : 1;

            var indexScores = new Dictionary<int, float>();
            foreach (var (term, tf) in termFreqs)
            {
                var df = stats.DocFrequencies.TryGetValue(term, out var d) ? d : 0;
                var idf = MathF.Log((n + 1f) / (df + 1f)) + 1f;            // global idf
                var normTf = tf * (K1 + 1f) / (tf + K1 * (1f - B + B * docLen / avgDocLen));
                var idx = StableIndex(term);
                var score = idf * normTf;
                indexScores[idx] = indexScores.TryGetValue(idx, out var e) ? e + score : score;
            }
            var sorted = indexScores.OrderBy(static kv => kv.Key).ToArray();
            result[i] = new SparseVector(
                sorted.Select(static kv => kv.Key).ToArray(),
                sorted.Select(static kv => kv.Value).ToArray());
        }
        return result;
    }

    // Query vector: raw term frequency only, NO idf (idf is on the doc side → avoids IDF²).
    public static SparseVector ComputeQueryVector(string text)
    {
        var tokens = Tokenize(text);
        var termFreqs = new Dictionary<int, float>();
        foreach (var t in tokens)
        {
            var idx = StableIndex(t);
            termFreqs[idx] = termFreqs.TryGetValue(idx, out var c) ? c + 1f : 1f;
        }
        var sorted = termFreqs.OrderBy(static kv => kv.Key).ToArray();
        return new SparseVector(
            sorted.Select(static kv => kv.Key).ToArray(),
            sorted.Select(static kv => kv.Value).ToArray());
    }
}
