using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

/// <summary>
/// C# twin of the Python extractor's match normaliser (<c>textnorm.py</c>) and token F1
/// (<c>boundary_score.py</c>). Keep the rules identical. Note: .NET <c>\s</c> / <c>\w</c> differ
/// slightly from Python's on exotic Unicode; irrelevant for the book text we handle.
/// </summary>
public static class TextMatch
{
    // "-" at end of line, optional spaces/tabs before the newline (\r is NOT included), then any whitespace.
    private static readonly Regex EolHyphen = new(@"-[ \t]*\n\s*", RegexOptions.None);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.None);
    private static readonly Regex Word = new(@"\w+", RegexOptions.None);

    public static string Normalize(string s) =>
        Whitespace.Replace(EolHyphen.Replace(s, ""), " ").Trim();

    public static bool Matches(JsonNode? a, JsonNode? b)
    {
        var ka = Kind(a);
        var kb = Kind(b);

        if (ka == JsonValueKind.String && kb == JsonValueKind.String)
            return Normalize(a!.GetValue<string>()) == Normalize(b!.GetValue<string>());

        var aBool = ka is JsonValueKind.True or JsonValueKind.False;
        var bBool = kb is JsonValueKind.True or JsonValueKind.False;
        if (aBool || bBool) return aBool && bBool && ka == kb;

        if (ka == JsonValueKind.Array && kb == JsonValueKind.Array)
        {
            var la = a!.AsArray();
            var lb = b!.AsArray();
            if (la.Count != lb.Count) return false;
            for (var i = 0; i < la.Count; i++)
                if (!Matches(la[i], lb[i])) return false;
            return true;
        }

        if (ka == JsonValueKind.Object && kb == JsonValueKind.Object)
        {
            var oa = a!.AsObject();
            var ob = b!.AsObject();
            if (oa.Count != ob.Count) return false;
            foreach (var (key, va) in oa)
            {
                if (!ob.ContainsKey(key)) return false;
                if (!Matches(va, ob[key])) return false;
            }
            return true;
        }

        if (ka == JsonValueKind.Number && kb == JsonValueKind.Number)
            return a!.GetValue<double>() == b!.GetValue<double>();

        // Remaining: null/null matches; any other kind mismatch (incl. string/array/object vs other) does not.
        return ka == JsonValueKind.Null && kb == JsonValueKind.Null;
    }

    /// <summary>Token-multiset F1 over <c>\w+</c> lowercased tokens; 0 when either side is empty or disjoint.</summary>
    public static double TokenF1(string a, string b)
    {
        var ca = Count(a);
        var cb = Count(b);
        if (ca.Count == 0 || cb.Count == 0) return 0.0;
        var common = 0;
        foreach (var (token, n) in ca)
            if (cb.TryGetValue(token, out var m)) common += Math.Min(n, m);
        if (common == 0) return 0.0;
        var p = (double)common / cb.Values.Sum();
        var r = (double)common / ca.Values.Sum();
        return 2 * p * r / (p + r);
    }

    /// <summary>Every string leaf in document order, joined with single spaces.</summary>
    public static string FlattenText(JsonNode? v)
    {
        var parts = new List<string>();
        Collect(v, parts);
        return string.Join(' ', parts);
    }

    private static void Collect(JsonNode? v, List<string> parts)
    {
        switch (Kind(v))
        {
            case JsonValueKind.String:
                parts.Add(v!.GetValue<string>());
                break;
            case JsonValueKind.Array:
                foreach (var item in v!.AsArray()) Collect(item, parts);
                break;
            case JsonValueKind.Object:
                foreach (var (_, item) in v!.AsObject()) Collect(item, parts);
                break;
        }
    }

    private static Dictionary<string, int> Count(string s)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in Word.Matches(s.ToLowerInvariant()))
            d[m.Value] = d.GetValueOrDefault(m.Value) + 1;
        return d;
    }

    private static JsonValueKind Kind(JsonNode? n) => n?.GetValueKind() ?? JsonValueKind.Null;
}
