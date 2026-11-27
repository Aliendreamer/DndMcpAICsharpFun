using System.Text.Json;
using System.Text.RegularExpressions;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

/// <summary>
/// A class's level table reaches the chat with every value named. The stored text holds the printed table read
/// row by row ("17th +6 Brutal Critical 6 +4"), its headers lines away: the chat model mixes the columns up.
/// </summary>
public sealed partial class ClassTables
{
    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<string>>> _lines;

    public ClassTables(ILogger<ClassTables> logger) =>
        _lines = new(() => Load("5etools", logger));

    internal ClassTables(IReadOnlyDictionary<string, IReadOnlyList<string>> lines) => _lines = new(() => lines);

    /// <summary>The text with the printed class table swapped for named lines; any other text unchanged.</summary>
    public string Relabel(EntityEnvelope e, string text) =>
        e.Type == EntityType.Class && _lines.Value.TryGetValue(Key(e.SourceBook, e.Name), out var lines)
            ? Relabel(text, e.Name, lines)
            : text;

    internal static string Relabel(string text, string className, IReadOnlyList<string> lines)
    {
        // A caster's slot columns, printed beside the table, are read as a separate run of bare numbers.
        text = SlotColumns().Replace(text, "");

        // Stored texts hold the table on lines or run together on one line: rows are found by their level ordinals.
        var first = FirstRow().Match(text);
        if (!first.Success) return text;
        var last = LastRow().Match(text, first.Index);
        if (!last.Success) return text;

        // The title just above the first row ("The BArBArian Proficiency Rage Level Bonus ...").
        var start = first.Index;
        var lookBack = Math.Max(0, start - 160);
        var title = new Regex($@"\bThe\s+{Regex.Escape(className)}\b", RegexOptions.IgnoreCase | RegexOptions.RightToLeft)
            .Match(text, lookBack, start - lookBack);
        if (title.Success) start = title.Index;

        // The last row runs on until the first word that is not one of its cells: the prose resumes there.
        var cells = Words(lines[^1]).Append("—").Append("-").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var end = last.Index + last.Length;
        foreach (Match w in Word().Matches(text[end..]))
        {
            if (!cells.Contains(Bare(w.Value))) break;
            end = last.Index + last.Length + w.Index + w.Length;
        }

        var table = $"The {className} table, by level:\n" + string.Join("\n", lines);
        var before = text[..start].TrimEnd();
        var after = text[end..].TrimStart();
        return (before.Length > 0 ? before + "\n" : "") + table + (after.Length > 0 ? "\n" + after : "");
    }

    private static IEnumerable<string> Words(string line) =>
        Word().Matches(line).Select(m => Bare(m.Value)).Where(w => w.Length > 0);

    private static string Bare(string word) => word.Trim(',', ';', ':', '(', ')');

    private static string Key(string? book, string name) => $"{book?.ToUpperInvariant()}|{name.ToLowerInvariant()}";

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Load(string fivetoolsDir, ILogger logger)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var dir = Path.Combine(fivetoolsDir, "class");
        if (!Directory.Exists(dir))
        {
            logger.LogWarning("No 5etools class data at {Dir}: class tables reach the chat as printed", dir);
            return map;
        }
        foreach (var file in Directory.EnumerateFiles(dir, "class-*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("class", out var classes) || classes.ValueKind != JsonValueKind.Array) continue;
            foreach (var c in classes.EnumerateArray())
                if (c.TryGetProperty("name", out var n) && c.TryGetProperty("source", out var s))
                    map.TryAdd(Key(s.GetString(), n.GetString()!), ClassProgressionTableProjector.LevelLines(c));
        }
        logger.LogInformation("Class tables loaded: {Count}", map.Count);
        return map;
    }

    [GeneratedRegex(@"(?<![\w+])1st\s+\+2\b")]
    private static partial Regex FirstRow();

    [GeneratedRegex(@"(?<![\w+])20th\s+\+6\b")]
    private static partial Regex LastRow();

    [GeneratedRegex(@"(?:\b(?:Cantrips|Spells|Infused)\s+){0,3}-Spell Slots per Spell Level-(?:\s+(?:Known|Items|\d+(?:st|nd|rd|th)|\d+)(?=\s|$))+\s*")]
    private static partial Regex SlotColumns();

    [GeneratedRegex(@"\S+")]
    private static partial Regex Word();
}