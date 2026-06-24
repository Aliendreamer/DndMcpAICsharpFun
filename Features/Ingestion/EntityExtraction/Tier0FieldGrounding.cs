using System.Text;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Tier 0 of the grounding gate (parent prose-grounded-knowledge-model design.md §G): a cheap,
/// OCR-tolerant check that an emitted text value is actually supported by the candidate's source
/// prose. Tolerance for the systematic OCR corruption in <c>dnd_blocks</c> (e.g. "Brealh"→"Breath",
/// "fI."→"ft.") comes from per-token Levenshtein distance, NOT a brittle hand-maintained confusable
/// map. This catches gross fabrication (values with no support in the source); fine-grained facts
/// and bare numbers/codes are out of Tier 0 scope and escalate to later tiers.
/// </summary>
public static class Tier0FieldGrounding
{
    private const int MinSignificantTokenLength = 3;

    /// <summary>
    /// Returns true when every significant token (length ≥ 3) of <paramref name="value"/> has an
    /// OCR-fuzzy match somewhere in <paramref name="sourceText"/>. An empty/insignificant value or
    /// empty source is treated as NOT grounded (nothing to support → route to review).
    /// </summary>
    public static bool IsTextGrounded(string value, string sourceText)
    {
        var valueTokens = SignificantTokens(value);
        if (valueTokens.Count == 0) return false;

        var sourceTokens = Tokenize(sourceText);
        if (sourceTokens.Count == 0) return false;

        foreach (var token in valueTokens)
        {
            var grounded = false;
            foreach (var source in sourceTokens)
            {
                if (WithinOcrDistance(token, source)) { grounded = true; break; }
            }
            if (!grounded) return false;
        }
        return true;
    }

    private static bool WithinOcrDistance(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        var threshold = Math.Max(1, Math.Min(a.Length, b.Length) / 4);
        return Levenshtein(a, b) <= threshold;
    }

    private static List<string> SignificantTokens(string text)
    {
        var result = new List<string>();
        foreach (var token in Tokenize(text))
            if (token.Length >= MinSignificantTokenLength)
                result.Add(token);
        return result;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[a.Length, b.Length];
    }
}
