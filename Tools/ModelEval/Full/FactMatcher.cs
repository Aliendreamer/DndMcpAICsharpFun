using System.Text;

namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

internal static class FactMatcher
{
    /// <summary>
    /// Lowercases, removes whitespace, commas and markdown/label noise (<c>* _ ` :</c>), and folds
    /// Unicode hyphens/minus (U+2010-U+2015, U+2212) to ASCII '-'.
    /// </summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c) || c is ',' or '*' or '_' or '`' or ':') continue;
            sb.Append(c is (>= '\u2010' and <= '\u2015') or '\u2212' ? '-' : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// True when every fact has at least one variant contained in the normalized haystack.
    /// A numeric edge of a variant must not be glued to another digit (so "d6" does not match "d60").
    /// </summary>
    public static bool AllMatch(IReadOnlyList<Fact> facts, string haystack)
    {
        var h = Normalize(haystack);
        return facts.All(f => f.Variants.Any(v => ContainsClean(h, Normalize(v))));
    }

    private static bool ContainsClean(string h, string v)
    {
        if (v.Length == 0) return false;
        var start = 0;
        while (true)
        {
            var idx = h.IndexOf(v, start, StringComparison.Ordinal);
            if (idx < 0) return false;
            var end = idx + v.Length;
            var leftOk = !(char.IsAsciiDigit(v[0]) && idx > 0 && char.IsAsciiDigit(h[idx - 1]));
            var rightOk = !(char.IsAsciiDigit(v[^1]) && end < h.Length && char.IsAsciiDigit(h[end]));
            if (leftOk && rightOk) return true;
            start = idx + 1;
        }
    }
}
