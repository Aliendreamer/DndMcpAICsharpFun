using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Synthesizes one wide progression CanonicalTable per class (levels 1-20).</summary>
public static class ClassProgressionTableProjector
{
    public static CanonicalTable Project(JsonElement classEntry, string bookKey)
    {
        var name = classEntry.GetProperty("name").GetString()!;
        var prov = new ProvenanceRef($"{EntityIdSlug.BookSlug(bookKey)}.5etools", bookKey, null);
        var featuresByLevel = FeaturesByLevel(classEntry);

        var columns = new List<string> { "Level", "Proficiency Bonus", "Features" };
        var rows = new List<CanonicalTableRow>();
        for (var level = 1; level <= 20; level++)
        {
            var cells = new List<CanonicalCell>
            {
                new(level.ToString(), prov),
                new($"+{2 + (level - 1) / 4}", prov),
                new(featuresByLevel.TryGetValue(level, out var f) ? string.Join(", ", f) : "", prov),
            };
            rows.Add(new CanonicalTableRow(cells));
        }
        return new CanonicalTable(EntityIdSlug.Table(bookKey, name), name, columns, rows);
    }

    private static Dictionary<int, List<string>> FeaturesByLevel(JsonElement classEntry)
    {
        var map = new Dictionary<int, List<string>>();
        if (!classEntry.TryGetProperty("classFeatures", out var cf) || cf.ValueKind != JsonValueKind.Array)
            return map;
        foreach (var e in cf.EnumerateArray())
        {
            var spec = e.ValueKind == JsonValueKind.String ? e.GetString()
                     : e.TryGetProperty("classFeature", out var s) ? s.GetString() : null;
            if (spec is null) continue;
            var parts = spec.Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[^1], out var level)) continue;
            (map.TryGetValue(level, out var list) ? list : map[level] = new List<string>()).Add(parts[0]);
        }
        return map;
    }
}
