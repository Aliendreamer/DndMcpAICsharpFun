namespace DndMcpAICsharpFun.Features.Ingestion;

public static class Bm25Vectorizer
{
    private const float K1 = 1.5f;
    private const float B = 0.75f;
    private const int VocabSize = 30000;

    public static IReadOnlyList<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var lower = text.ToLowerInvariant();
        var start = -1;
        for (var i = 0; i <= lower.Length; i++)
        {
            var isLetter = i < lower.Length && char.IsLetter(lower[i]);
            if (isLetter && start == -1)
            {
                start = i;
            }
            else if (!isLetter && start != -1)
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
                var idx = Math.Abs(term.GetHashCode()) % VocabSize;
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
