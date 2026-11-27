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
        var rows = text.Split('\n');
        var first = Array.FindIndex(rows, r => FirstRow().IsMatch(r));
        if (first < 0) return text;
        var last = Array.FindIndex(rows, first, r => LastRow().IsMatch(r));
        if (last < 0) return text;

        // The title and header lines just above the first row ("The Barbarian", "Proficiency Rage", "Level Bonus ...").
        var start = first;
        var title = Letters(className);
        for (var k = first - 1; k >= 0 && k >= first - 4; k--)
        {
            if (Letters(rows[k]) == "the" + title) { start = k; break; }
            if (!rows[k].Contains("Level", StringComparison.OrdinalIgnoreCase)
                && !rows[k].Contains("Proficiency", StringComparison.OrdinalIgnoreCase)) break;
            start = k;
        }

        var table = $"The {className} table, by level:\n" + string.Join("\n", lines);
        return string.Join("\n", rows[..start].Append(table).Concat(rows[(last + 1)..]));
    }

    private static string Letters(string s) => new([.. s.ToLowerInvariant().Where(char.IsLetter)]);

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

    [GeneratedRegex(@"^1st \+2\b")]
    private static partial Regex FirstRow();

    [GeneratedRegex(@"^20th \+6\b")]
    private static partial Regex LastRow();
}