using System.Text;
using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

/// <summary>
/// Completes a rendered entity text: every printed text of the fields that the type's renderer did not show is appended,
/// one line per paragraph, list item or table row. The renderer's own opening stays as it is.
/// </summary>
internal static class EntityFullText
{
    /// <summary>Codes, references and values the renderers already show in their own words.</summary>
    private static readonly HashSet<string> SkippedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "source", "page", "size", "edition", "type", "category", "rarity", "school", "time", "range", "components",
        "duration", "className", "classSource", "raceName", "raceSource", "shortName", "subclassShortName", "classFeatures",
        "subclassFeatures", "srd", "basicRules", "otherSources", "reprintedAs", "additionalSources", "alignment", "pantheon",
        "symbol", "domains", "headquarters", "goals", "setting", "ruleType", "trapHazType", "vehicleType", "weaponCategory",
        "dmg1", "dmg2", "dmgType", "property", "ammoType", "ac", "hp", "speed", "immune", "resist", "vulnerable",
        "conditionImmune", "description", "prerequisite", "classes", "damageInflict", "keywords", "traitTags", "languageTags",
        "senseTags", "miscTags", "token", "age", "style", "hd", "casterProgression",
    };

    public static string Complete(string rendered, JsonElement fields)
    {
        if (fields.ValueKind != JsonValueKind.Object) return rendered;
        var lines = new List<string>();
        foreach (var p in fields.EnumerateObject())
        {
            if (p.Name.StartsWith('_') || SkippedKeys.Contains(p.Name)) continue;
            if (p.Name is "entries" or "entriesHigherLevel") Collect(p.Value, "", rendered, lines);
            else Collect(p.Value, Label(p.Name) + ": ", rendered, lines);
        }
        if (lines.Count == 0) return rendered;
        var sb = new StringBuilder(rendered.TrimEnd());
        foreach (var line in lines) sb.Append('\n').Append(line);
        return sb.ToString();
    }

    /// <summary>Adds the element's texts; <paramref name="lead"/> (a block's name, a field's label) opens the first line.</summary>
    private static void Collect(JsonElement e, string lead, string rendered, List<string> lines)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                var text = RendererHelpers.StripTags(e.GetString()!).Trim();
                if (text.Length > 0 && !rendered.Contains(text, StringComparison.Ordinal)) lines.Add(lead + text);
                break;
            case JsonValueKind.Array when ShortValues(e) is { } values:
                if (!rendered.Contains(values, StringComparison.Ordinal)) lines.Add(lead + values);
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray())
                {
                    var before = lines.Count;
                    Collect(x, lead, rendered, lines);
                    if (lines.Count > before) lead = "";
                }
                break;
            case JsonValueKind.Object:
                CollectObject(e, lead, rendered, lines);
                break;
        }
    }

    private static void CollectObject(JsonElement e, string lead, string rendered, List<string> lines)
    {
        var name = RendererHelpers.StringProp(e, "name");
        if (name.Length == 0) name = RendererHelpers.StringProp(e, "caption");
        if (name.Length > 0) lead += name.EndsWith('.') || name.EndsWith(':') ? name + " " : name + ". ";
        var before = lines.Count;
        foreach (var p in e.EnumerateObject())
        {
            if (p.Name is "name" or "caption" or "type" or "style" or "source" or "page" or "colStyles" || p.Name.StartsWith('_')) continue;
            if (p.Name is "rows" or "colLabels") AddRows(p.Name, p.Value, lead, lines);
            else Collect(p.Value, lead, rendered, lines);
            if (lines.Count > before) lead = "";
        }
        if (lines.Count == before && name.Length > 0 && !rendered.Contains(name, StringComparison.Ordinal)) lines.Add(lead.TrimEnd());
    }

    private static void AddRows(string key, JsonElement value, string lead, List<string> lines)
    {
        if (value.ValueKind != JsonValueKind.Array) return;
        var rows = key == "colLabels" ? [value] : value.EnumerateArray().ToList();
        foreach (var row in rows)
        {
            var cells = row.ValueKind == JsonValueKind.Object && row.TryGetProperty("row", out var r) ? r : row;
            if (cells.ValueKind != JsonValueKind.Array) continue;
            var text = string.Join(" | ", cells.EnumerateArray().Select(c => RendererHelpers.EntriesText([c])));
            if (text.Trim(' ', '|').Length == 0) continue;
            lines.Add(lead + text);
            lead = "";
        }
    }

    /// <summary>A list of two or more words or short phrases ("daggers", "darts") reads as one line; null for anything else.</summary>
    private static string? ShortValues(JsonElement array)
    {
        var items = array.EnumerateArray().ToList();
        return items.Count > 1 && items.All(x => x.ValueKind == JsonValueKind.String
                && x.GetString() is { Length: > 0 and <= 40 } s && !s.EndsWith('.') && !s.Contains(". ", StringComparison.Ordinal))
            ? string.Join(", ", items.Select(x => RendererHelpers.StripTags(x.GetString()!).Trim()))
            : null;
    }

    private const int MinPrintedLine = 9;
    private const int ShortTail = 25;

    /// <summary>
    /// Appends every run of printed lines (the extractor's text of the page) that the text does not hold, hyphenation
    /// undone: introductions and tables the structured fields never carried.
    /// </summary>
    public static string AppendPrinted(string text, string printed)
    {
        var held = Reduce(text);
        var sb = new StringBuilder(text.TrimEnd());
        var run = new List<string>();
        void Flush()
        {
            if (run.Any(l => Reduce(l).Length >= MinPrintedLine)) sb.Append('\n').Append(Join(run));
            run.Clear();
        }
        foreach (var line in printed.Split('\n').Select(l => l.Trim()))
        {
            var reduced = Reduce(line);
            if (reduced.Length == 0) continue;
            // A short line (a table cell, a sentence's last words) goes with the run it stands in: found elsewhere in
            // the text it proves nothing.
            var goesOn = run.Count > 0
                && (reduced.Length < MinPrintedLine || reduced.Length < ShortTail && run[^1][^1] is not ('.' or '!' or '?' or ':'));
            if (!goesOn && (reduced.Length < MinPrintedLine || held.Contains(reduced, StringComparison.Ordinal))) Flush();
            else run.Add(line);
        }
        Flush();
        return sb.ToString();
    }

    private static string Join(List<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (sb.Length > 0 && sb[^1] == '-' && char.IsLower(line[0])) sb.Length--;
            else if (sb.Length > 0) sb.Append(' ');
            sb.Append(line);
        }
        return sb.ToString();
    }

    private static string Reduce(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>"entriesHigherLevel" reads "Entries higher level".</summary>
    private static string Label(string key)
    {
        var sb = new StringBuilder();
        foreach (var c in key)
        {
            if (char.IsUpper(c)) sb.Append(' ');
            sb.Append(sb.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}