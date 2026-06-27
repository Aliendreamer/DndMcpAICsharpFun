using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// In-memory index loaded from the local 5etools JSON corpus, mapping a normalised
/// entity name to its canonical display name and <see cref="EntityType"/>.
/// Built once; the index is immutable after construction.
/// </summary>
public sealed class EntityNameIndex
{
    public IReadOnlyDictionary<string, (string Canonical, EntityType Type)> Entries { get; }

    public EntityNameIndex(string fivetoolsDir)
    {
        var entries = new Dictionary<string, (string Canonical, EntityType Type)>(StringComparer.Ordinal);

        // spells
        LoadGlob(entries, Path.Combine(fivetoolsDir, "spells"), "spells-*.json", "spell",
            _ => EntityType.Spell);

        // classes — loaded before monsters so "Bard" (class) wins over "Bard" (monster)
        LoadGlob(entries, Path.Combine(fivetoolsDir, "class"), "class-*.json", "class",
            _ => EntityType.Class);

        // monsters (all bestiary source files)
        LoadGlob(entries, Path.Combine(fivetoolsDir, "bestiary"), "bestiary-*.json", "monster",
            _ => EntityType.Monster);

        // magic/mundane items (classified by rarity)
        LoadGlob(entries, fivetoolsDir, "items.json", "item",
            e => FivetoolsEntityTypeMap.ForItem(
                e.TryGetProperty("rarity", out var r) ? r.GetString() : null));

        // base items — always mundane
        LoadGlob(entries, fivetoolsDir, "items-base.json", "baseitem",
            _ => EntityType.Item);

        // remaining top-level entity files (single-file each)
        LoadGlob(entries, fivetoolsDir, "backgrounds.json",        "background", _ => EntityType.Background);
        LoadGlob(entries, fivetoolsDir, "races.json",              "race",       _ => EntityType.Race);
        // feats: exclude Fighting-Style sub-features (category exactly "FS")
        LoadGlob(entries, fivetoolsDir, "feats.json", "feat", _ => EntityType.Feat,
            include: e => !e.TryGetProperty("category", out var cat) ||
                          (cat.GetString() ?? string.Empty) != "FS");
        LoadGlob(entries, fivetoolsDir, "conditionsdiseases.json", "condition",  _ => EntityType.Condition);
        LoadGlob(entries, fivetoolsDir, "deities.json",            "deity",      _ => EntityType.God);

        Entries = entries;
    }

    /// <summary>
    /// Normalises a name for index lookup: lowercase, keep only letters and digits.
    /// </summary>
    public static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // ---------------------------------------------------------------------------

    private static void LoadGlob(
        Dictionary<string, (string Canonical, EntityType Type)> entries,
        string directory,
        string pattern,
        string arrayKey,
        Func<JsonElement, EntityType> typeSelector,
        Func<JsonElement, bool>? include = null)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var file in Directory.GetFiles(directory, pattern).Order(StringComparer.Ordinal))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllBytes(file)); }
            catch (JsonException) { continue; }
            using (doc)
            {
                if (!doc.RootElement.TryGetProperty(arrayKey, out var arr)) continue;

                foreach (var elem in arr.EnumerateArray())
                {
                    if (include is not null && !include(elem)) continue;
                    if (!elem.TryGetProperty("name", out var nameProp)) continue;
                    var name = nameProp.GetString();
                    if (string.IsNullOrEmpty(name)) continue;

                    // first-wins: keep the entry already in the dictionary if present
                    entries.TryAdd(Normalize(name), (name, typeSelector(elem)));
                }
            }
        }
    }
}
