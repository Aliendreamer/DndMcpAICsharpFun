using System.Text;
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

internal static partial class FactMatcher
{
    [GeneratedRegex(@"\\u([0-9a-fA-F]{4})")]
    private static partial Regex JsonUnicodeEscape();

    /// <summary>
    /// Lowercases, removes whitespace, commas and markdown/label noise (<c>* _ ` :</c>), and folds
    /// Unicode hyphens/minus (U+2010-U+2015, U+2212) to ASCII '-'.
    /// </summary>
    public static string Normalize(string s)
    {
        s = JsonUnicodeEscape().Replace(s, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c) || c is ',' or '*' or '_' or '`' or ':') continue;
            sb.Append(c is (>= '\u2010' and <= '\u2015') or '\u2212' ? '-'
                : c is '‘' or '’' ? '\''
                : char.ToLowerInvariant(c));
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
        return facts.All(f => f.Variants.Any(v => Matches(h, v)));
    }

    /// <summary>A variant prefixed <c>re:</c> is a regex applied to the NORMALIZED haystack (no whitespace,
    /// lowercase), e.g. <c>re:ac\d+</c> for "AC 17"; any other variant is a normalized substring match.</summary>
    private static bool Matches(string normalizedHaystack, string variant) =>
        variant.StartsWith("re:", StringComparison.Ordinal)
            ? Regex.IsMatch(normalizedHaystack, variant[3..], RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            : ContainsClean(normalizedHaystack, Normalize(variant));

    private static bool ContainsClean(string h, string v)
    {
        if (v.Length == 0) return false;
        var start = 0;
        while (true)
        {
            var idx = h.IndexOf(v, start, StringComparison.Ordinal);
            if (idx < 0) return false;
            var end = idx + v.Length;
            var leftOk = !(char.IsAsciiDigit(v[0]) && idx > 0
                && (char.IsAsciiDigit(h[idx - 1]) || (h[idx - 1] == '/' && idx > 1 && char.IsAsciiDigit(h[idx - 2]))));
            var rightOk = !(char.IsAsciiDigit(v[^1]) && end < h.Length
                && (char.IsAsciiDigit(h[end]) || (h[end] == '/' && end + 1 < h.Length && char.IsAsciiDigit(h[end + 1]))));
            if (leftOk && rightOk) return true;
            start = idx + 1;
        }
    }
}