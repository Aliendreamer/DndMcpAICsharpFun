using System.Text.Json;
using System.Text.RegularExpressions;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Projects each spellcasting subclass's 5etools additionalSpells into a
/// &lt;slug&gt;.table.&lt;subclass-slug&gt;-spells CanonicalTable (cols level|spells) for a projected book.</summary>
public static partial class SubclassSpellsProjector
{
    [GeneratedRegex(@"^s(\d+)$")] private static partial Regex SpellLevelKey();

    public static IReadOnlyList<CanonicalTable> Project(string fivetoolsDir, string sourceKey)
    {
        var bookSlug = EntityIdSlug.Table(sourceKey, "x").Split('.')[0];
        var classDir = Path.Combine(fivetoolsDir, "class");
        if (!Directory.Exists(classDir)) return [];

        var tables = new List<CanonicalTable>();
        foreach (var file in Directory.EnumerateFiles(classDir, "class-*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("subclass", out var subs) || subs.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var sc in subs.EnumerateArray())
            {
                if (!(sc.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() == sourceKey))
                    continue;
                if (!sc.TryGetProperty("name", out var nm) || nm.ValueKind != JsonValueKind.String) continue;
                if (!sc.TryGetProperty("additionalSpells", out var add) || add.ValueKind != JsonValueKind.Array) continue;

                var byLevel = new SortedDictionary<int, List<string>>();
                foreach (var grant in add.EnumerateArray())
                {
                    if (grant.ValueKind != JsonValueKind.Object) continue;
                    foreach (var kind in new[] { "prepared", "known", "expanded" })
                    {
                        if (!grant.TryGetProperty(kind, out var m) || m.ValueKind != JsonValueKind.Object) continue;
                        foreach (var lvl in m.EnumerateObject())
                        {
                            if (!TryLevel(kind, lvl.Name, out var level)) continue; // skip choose/ability keys
                            if (lvl.Value.ValueKind != JsonValueKind.Array) continue;
                            var list = byLevel.TryGetValue(level, out var l) ? l : byLevel[level] = new();
                            foreach (var sp in lvl.Value.EnumerateArray())
                                if (sp.ValueKind == JsonValueKind.String) list.Add(sp.GetString()!);
                        }
                    }
                }
                if (byLevel.Count == 0) continue;

                var page = sc.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : (int?)null;
                var prov = new ProvenanceRef($"{bookSlug}.5etools", sourceKey, page);
                var rows = byLevel.Where(kv => kv.Value.Count > 0).Select(kv => new CanonicalTableRow(
                    new List<CanonicalCell>
                    {
                        new(kv.Key.ToString(), prov),
                        new(string.Join(", ", kv.Value.Distinct()), prov),
                    })).ToList();
                if (rows.Count == 0) continue; // subclass with only choose-blocks → no table

                var id = $"{bookSlug}.table.{EntityIdSlug.Slug(nm.GetString()!)}-spells";
                tables.Add(new CanonicalTable(id, $"{nm.GetString()} Spells", new List<string> { "level", "spells" }, rows));
            }
        }
        return tables;
    }

    // prepared/known keys are character levels; expanded keys are "s<N>" (spell level N ⇒ Warlock level 2N-1).
    private static bool TryLevel(string kind, string key, out int level)
    {
        if (kind == "expanded")
        {
            var m = SpellLevelKey().Match(key);
            if (m.Success) { level = 2 * int.Parse(m.Groups[1].Value) - 1; return true; }
            level = 0; return false;
        }
        return int.TryParse(key, out level);
    }
}