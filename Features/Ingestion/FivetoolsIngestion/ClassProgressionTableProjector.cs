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

    private readonly record struct GroupCol(string Label, Func<int, string> Value, bool SpellSlot);

    /// <summary>
    /// One line per level with every value named ("Level 17: proficiency bonus +6; features: Brutal Critical;
    /// Rages 6; Rage Damage +4"). A row of bare numbers under headers far above it is unreadable for a small model.
    /// </summary>
    public static IReadOnlyList<string> LevelLines(JsonElement classEntry)
    {
        var featuresByLevel = FeaturesByLevel(classEntry);
        var groupCols = GroupColumns(classEntry);
        var lines = new List<string>(20);
        for (var level = 1; level <= 20; level++)
        {
            var parts = new List<string> { $"proficiency bonus +{2 + (level - 1) / 4}" };
            if (featuresByLevel.TryGetValue(level, out var f)) parts.Add("features: " + string.Join(", ", f));
            foreach (var g in groupCols.Where(g => !g.SpellSlot))
                if (g.Value(level) is { Length: > 0 } v) parts.Add($"{g.Label} {v}");
            var slots = groupCols.Where(g => g.SpellSlot)
                .Select(g => (g.Label, Value: g.Value(level))).Where(s => s.Value.Length > 0)
                .Select(s => $"{s.Label}-level {s.Value}").ToList();
            if (slots.Count > 0) parts.Add("spell slots: " + string.Join(", ", slots));
            lines.Add($"Level {level}: {string.Join("; ", parts)}");
        }
        return lines;
    }

    // 5etools cells: numbers (0 = none), strings with markup, or typed objects for bonuses, speeds and dice.
    private static string Cell(JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                var n = v.GetInt32();
                return n == 0 ? "" : n.ToString();
            case JsonValueKind.String:
                return FivetoolsJson.StripMarkup(v.GetString()!);
            case JsonValueKind.Object when v.TryGetProperty("type", out var type):
                var value = v.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Number ? val.GetInt32() : 0;
                switch (type.GetString())
                {
                    case "bonus": return $"+{value}";
                    case "bonusSpeed": return value == 0 ? "" : $"+{value} ft.";
                    case "dice" when v.TryGetProperty("toRoll", out var roll) && roll.ValueKind == JsonValueKind.Array:
                        return string.Join(" + ", roll.EnumerateArray().Select(d =>
                            $"{d.GetProperty("number").GetInt32()}d{d.GetProperty("faces").GetInt32()}"));
                }
                break;
        }
        return v.ToString();
    }

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
                    return Cell(row[c]);
                }, g.TryGetProperty("title", out var t) && t.GetString() is { } title
                    && title.Contains("Spell Slots", StringComparison.OrdinalIgnoreCase)));
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
            // 5etools classFeature: "Name|ClassName|ClassSource|Level[|FeatureSource]".
            if (parts.Length < 4 || !int.TryParse(parts[3], out var level)) continue;
            // Skip cross-source additions (e.g. Tasha's options grafted onto a PHB class) —
            // a per-book printed progression table lists only that book's own features.
            if (parts.Length >= 5 && !string.IsNullOrEmpty(parts[4])) continue;
            (map.TryGetValue(level, out var list) ? list : map[level] = new List<string>()).Add(parts[0]);
        }
        return map;
    }
}