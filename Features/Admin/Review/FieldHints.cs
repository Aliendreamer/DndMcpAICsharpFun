using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Admin.Review;

/// <summary>
/// Short hints for the review page: what shape a field takes (from the type's fields schema) and an example
/// value taken from an accepted entity of the same type.
/// </summary>
public static class FieldHints
{
    private const int ExampleMaxChars = 240;

    /// <summary>"integer", "array of string | object", "string, one of: a, b"… from one schema property.</summary>
    public static string Describe(JsonElement prop)
    {
        var kinds = Kinds(prop);
        if (kinds.Count == 0 && prop.TryGetProperty("$ref", out var r) && r.ValueKind == JsonValueKind.String)
            kinds.Add(r.GetString()!.Split('/')[^1]);
        if (kinds.Count == 0 && (prop.TryGetProperty("oneOf", out var alt) || prop.TryGetProperty("anyOf", out alt))
            && alt.ValueKind == JsonValueKind.Array)
            kinds.AddRange(alt.EnumerateArray().SelectMany(Kinds).Distinct());

        var text = kinds.Count == 0 ? "any JSON" : string.Join(" | ", kinds);
        if (kinds.Contains("array") && prop.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
        {
            var inner = Describe(items);
            text = text.Replace("array", $"array of {inner}", StringComparison.Ordinal);
        }
        if (prop.TryGetProperty("enum", out var en) && en.ValueKind == JsonValueKind.Array)
            text += ", one of: " + string.Join(", ", en.EnumerateArray()
                .Where(v => v.ValueKind != JsonValueKind.Null).Select(v => v.ToString()));
        if (prop.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
            text += " - " + d.GetString();
        return text;
    }

    private static List<string> Kinds(JsonElement prop)
    {
        if (!prop.TryGetProperty("type", out var t)) return [];
        var kinds = t.ValueKind switch
        {
            JsonValueKind.String => [t.GetString()!],
            JsonValueKind.Array => t.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!).ToList(),
            _ => new List<string>(),
        };
        kinds.Remove("null");
        return kinds;
    }

    /// <summary>A compact example value, cut to a readable length.</summary>
    public static string Example(JsonElement value)
    {
        var json = JsonSerializer.Serialize(value);
        return json.Length <= ExampleMaxChars ? json : json[..ExampleMaxChars] + "…";
    }
}
