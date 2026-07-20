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
        var groupCols = GroupColumns(classEntry); // (label, valueForLevel) in order

        var columns = new List<string> { "Level", "Proficiency Bonus", "Features" };
        columns.AddRange(groupCols.Select(g => g.Label));

        var rows = new List<CanonicalTableRow>();
        for (var level = 1; level <= 20; level++)
        {
            var cells = new List<CanonicalCell>
            {
                new(level.ToString(), prov),
                new($"+{2 + (level - 1) / 4}", prov),
                new(featuresByLevel.TryGetValue(level, out var f) ? string.Join(", ", f) : "", prov),
            };
            foreach (var g in groupCols) cells.Add(new CanonicalCell(g.Value(level), prov));
            rows.Add(new CanonicalTableRow(cells));
        }
        return new CanonicalTable(EntityIdSlug.Table(bookKey, name), name, columns, rows);
    }

    private readonly record struct GroupCol(string Label, Func<int, string> Value);

    private static List<GroupCol> GroupColumns(JsonElement classEntry)
    {
        var cols = new List<GroupCol>();
        if (!classEntry.TryGetProperty("classTableGroups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            return cols;
        foreach (var g in groups.EnumerateArray())
        {
            var labels = g.TryGetProperty("colLabels", out var cl)
                ? FivetoolsJson.StringList(cl).Select(FivetoolsJson.StripMarkup).ToList()
                : new List<string>();
            var hasRows = g.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array;
            var hasProg = g.TryGetProperty("rowsSpellProgression", out var prog) && prog.ValueKind == JsonValueKind.Array;
            if (!hasRows && !hasProg) continue; // skip malformed group, continue
            var data = hasRows ? rows : prog;
            for (var col = 0; col < labels.Count; col++)
            {
                var c = col; var d = data; // capture
                cols.Add(new GroupCol(labels[c], level =>
                {
                    var idx = level - 1;
                    if (idx < 0 || idx >= d.GetArrayLength()) return "";
                    var row = d[idx];
                    if (row.ValueKind != JsonValueKind.Array || c >= row.GetArrayLength()) return "";
                    var v = row[c];
                    if (v.ValueKind == JsonValueKind.Number) { var n = v.GetInt32(); return n == 0 ? "" : n.ToString(); }
                    return v.ValueKind == JsonValueKind.String ? FivetoolsJson.StripMarkup(v.GetString()!) : v.ToString();
                }));
            }
        }
        return cols;
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
