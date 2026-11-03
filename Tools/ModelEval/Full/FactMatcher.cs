using System.Text;

namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

internal static class FactMatcher
{
    /// <summary>Lowercases and removes all whitespace and commas.</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c) || c == ',') continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>True when every fact has at least one variant contained in the normalized haystack.</summary>
    public static bool AllMatch(IReadOnlyList<Fact> facts, string haystack)
    {
        var h = Normalize(haystack);
        return facts.All(f => f.Variants.Any(v => h.Contains(Normalize(v), StringComparison.Ordinal)));
    }
}
