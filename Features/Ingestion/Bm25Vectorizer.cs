using System.Text;

namespace DndMcpAICsharpFun.Features.Ingestion;

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

    public static SparseVector[] ComputeBatch(IReadOnlyList<string> texts)
    {
        var tokenizedDocs = new IReadOnlyList<string>[texts.Count];
        for (var i = 0; i < texts.Count; i++)
            tokenizedDocs[i] = Tokenize(texts[i]);

        var docFrequencies = new Dictionary<string, int>();
        foreach (var tokens in tokenizedDocs)
        {
            foreach (var term in new HashSet<string>(tokens))
            {
                docFrequencies[term] = docFrequencies.TryGetValue(term, out var df) ? df + 1 : 1;
            }
        }

        var docLengths = new int[tokenizedDocs.Length];
        for (var i = 0; i < tokenizedDocs.Length; i++)
            docLengths[i] = tokenizedDocs[i].Count;

        var avgDocLen = tokenizedDocs.Length == 0 ? 1f : (float)docLengths.Average();
        var n = tokenizedDocs.Length;

        var result = new SparseVector[texts.Count];
        for (var i = 0; i < tokenizedDocs.Length; i++)
        {
            var tokens = tokenizedDocs[i];
            var docLen = docLengths[i];

            var termFreqs = new Dictionary<string, int>();
            foreach (var term in tokens)
                termFreqs[term] = termFreqs.TryGetValue(term, out var tf) ? tf + 1 : 1;

            var indexScores = new Dictionary<int, float>();
            foreach (var (term, tf) in termFreqs)
            {
                var df = docFrequencies[term];
                var idf = MathF.Log((n + 1f) / (df + 1f)) + 1f;
                var normTf = tf * (K1 + 1f) / (tf + K1 * (1f - B + B * docLen / avgDocLen));
                var score = idf * normTf;
                var idx = StableIndex(term);
                indexScores[idx] = indexScores.TryGetValue(idx, out var existing) ? existing + score : score;
            }

            var sorted = indexScores.OrderBy(static kv => kv.Key).ToArray();
            result[i] = new SparseVector(
                sorted.Select(static kv => kv.Key).ToArray(),
                sorted.Select(static kv => kv.Value).ToArray());
        }

        return result;
    }
}
