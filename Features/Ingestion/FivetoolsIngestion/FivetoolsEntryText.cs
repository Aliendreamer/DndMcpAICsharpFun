using System.Text.Json;
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>
/// Recursively flattens a 5etools <c>entries</c> JSON array — plain strings, nested
/// <c>entries</c>/<c>section</c> blocks, <c>list</c>, <c>table</c>, and <c>item</c>/<c>itemSpell</c>
/// shapes — into a single description string, stripping 5etools inline tags (e.g. <c>{@dice 1d4}</c>,
/// <c>{@item Rope|PHB}</c>) down to their display text. Unrecognised shapes are skipped rather than
/// throwing, so malformed or future 5etools entry types degrade gracefully.
/// </summary>
public static partial class FivetoolsEntryText
{
    [GeneratedRegex(@"\{@\w+\s+([^}|]+)(\|[^}]*)?\}")]
    private static partial Regex InlineTagPattern();

    /// <summary>
    /// Flattens a 5etools <c>entries</c> array into a single string. Returns <c>""</c> when
    /// <paramref name="entriesArray"/> is not a JSON array (including an absent/default element).
    /// </summary>
    public static string Flatten(JsonElement entriesArray)
    {
        if (entriesArray.ValueKind != JsonValueKind.Array)
            return "";

        var pieces = new List<string>();
        foreach (var entry in entriesArray.EnumerateArray())
        {
            var piece = FlattenEntry(entry);
            if (!string.IsNullOrWhiteSpace(piece))
                pieces.Add(piece);
        }

        return string.Join("\n\n", pieces);
    }

    private static string FlattenEntry(JsonElement entry) => entry.ValueKind switch
    {
        JsonValueKind.String => StripInlineTags(entry.GetString() ?? ""),
        JsonValueKind.Object => FlattenObject(entry),
        _ => "",
    };

    private static string FlattenObject(JsonElement entry)
    {
        var type = entry.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
            ? typeProp.GetString()
            : null;

        return type switch
        {
            "entries" or "section" => FlattenEntriesBlock(entry),
            "list" => FlattenList(entry),
            "table" => FlattenTable(entry),
            "item" or "itemSpell" => FlattenItem(entry),
            _ => FlattenUnknown(entry),
        };
    }

    private static string FlattenEntriesBlock(JsonElement entry)
    {
        var lines = new List<string>();

        if (entry.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            lines.Add(StripInlineTags(nameProp.GetString() ?? ""));

        if (entry.TryGetProperty("entries", out var nested) && nested.ValueKind == JsonValueKind.Array)
        {
            var inner = Flatten(nested);
            if (!string.IsNullOrWhiteSpace(inner))
                lines.Add(inner);
        }

        return string.Join("\n", lines);
    }

    private static string FlattenList(JsonElement entry)
    {
        if (!entry.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return "";

        var lines = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            var text = FlattenEntry(item);
            if (!string.IsNullOrWhiteSpace(text))
                lines.Add("• " + text);
        }

        return string.Join("\n", lines);
    }

    private static string FlattenTable(JsonElement entry)
    {
        var lines = new List<string>();

        if (entry.TryGetProperty("colLabels", out var colLabels) && colLabels.ValueKind == JsonValueKind.Array)
        {
            var header = colLabels.EnumerateArray().Select(FlattenCell).ToArray();
            if (header.Length > 0)
                lines.Add(string.Join(" | ", header));
        }

        if (entry.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array)
                    continue;

                var cells = row.EnumerateArray().Select(FlattenCell).ToArray();
                if (cells.Length > 0)
                    lines.Add(string.Join(" | ", cells));
            }
        }

        return string.Join("\n", lines);
    }

    private static string FlattenCell(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.String => StripInlineTags(cell.GetString() ?? ""),
        JsonValueKind.Object => FlattenObject(cell),
        JsonValueKind.Number => cell.GetRawText(),
        _ => "",
    };

    private static string FlattenItem(JsonElement entry)
    {
        var lines = new List<string>();

        if (entry.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            lines.Add(StripInlineTags(nameProp.GetString() ?? ""));

        if (entry.TryGetProperty("entry", out var singleEntry) && singleEntry.ValueKind == JsonValueKind.String)
        {
            lines.Add(StripInlineTags(singleEntry.GetString() ?? ""));
        }
        else if (entry.TryGetProperty("entries", out var nestedEntries) && nestedEntries.ValueKind == JsonValueKind.Array)
        {
            var inner = Flatten(nestedEntries);
            if (!string.IsNullOrWhiteSpace(inner))
                lines.Add(inner);
        }

        return string.Join("\n", lines);
    }

    private static string FlattenUnknown(JsonElement entry)
    {
        if (entry.TryGetProperty("entries", out var nestedEntries) && nestedEntries.ValueKind == JsonValueKind.Array)
            return Flatten(nestedEntries);

        if (entry.TryGetProperty("entry", out var singleEntry) && singleEntry.ValueKind == JsonValueKind.String)
            return StripInlineTags(singleEntry.GetString() ?? "");

        return "";
    }

    private static string StripInlineTags(string text) => InlineTagPattern().Replace(text, "$1");
}
