using System.Text.Json;
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Small helpers for reading 5etools JSON: strip {@tag ...} markup, read string arrays.</summary>
public static partial class FivetoolsJson
{
    // {@tagname display text|arg|arg} -> "display text"  (display is everything up to the first | or }).
    [GeneratedRegex(@"\{@\w+\s+([^|}]+)[^}]*\}")]
    private static partial Regex Tag();

    public static string StripMarkup(string label) => Tag().Replace(label, m => m.Groups[1].Value).Trim();

    public static IReadOnlyList<string> StringList(JsonElement arr)
    {
        if (arr.ValueKind != JsonValueKind.Array) return [];
        var list = new List<string>();
        foreach (var e in arr.EnumerateArray())
            list.Add(e.ValueKind == JsonValueKind.String ? e.GetString()! : e.ToString());
        return list;
    }
}
