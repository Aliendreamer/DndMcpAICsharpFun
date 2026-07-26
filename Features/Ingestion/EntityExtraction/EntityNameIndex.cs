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

    /// <summary>
    /// All distinct-by-type entries for a normalised name (load order preserved). Unlike
    /// <see cref="Entries"/> (first-wins, a single entry per name), this retains every type a
    /// name maps to, so a cross-type collision (e.g. "Dwarf" as both Monster and Race) can be
    /// resolved against a caller's preferred type.
    /// <para>
    /// Each entry also carries its 5etools <c>source</c> (empty when the record has none), so a
    /// caller can prefer the book currently being extracted over another book's entry of the same
    /// name — without which a heading is typed by whichever file happened to load first. NOTE the
    /// deliberate asymmetry with <see cref="Entries"/>, which stays a `(Canonical, Type)` pair: it is
    /// the first-wins convenience view and no production code reads it.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<(string Canonical, EntityType Type, string Source)>> EntriesByName { get; }

    public EntityNameIndex(string fivetoolsDir)
    {
        var entries = new Dictionary<string, (string Canonical, EntityType Type)>(StringComparer.Ordinal);
        var byName = new Dictionary<string, List<(string Canonical, EntityType Type, string Source)>>(StringComparer.Ordinal);

        // spells
        LoadGlob(entries, byName, Path.Combine(fivetoolsDir, "spells"), "spells-*.json", "spell",
            _ => EntityType.Spell);

        // classes — loaded before monsters so "Bard" (class) wins over "Bard" (monster)
        LoadGlob(entries, byName, Path.Combine(fivetoolsDir, "class"), "class-*.json", "class",
            _ => EntityType.Class);

        // subclasses — same files' subclass[] array; indexed by name AND shortName (e.g.
        // "Battlerager" for "Path of the Battlerager"), both grounding to the full canonical
        // name. Loaded after base classes so a colliding name resolves to Class first.
        LoadGlob(entries, byName, Path.Combine(fivetoolsDir, "class"), "class-*.json", "subclass",
            _ => EntityType.Subclass,
            aliasSelector: e => e.TryGetProperty("shortName", out var sn) ? sn.GetString() : null);

        // monsters (all bestiary source files)
        LoadGlob(entries, byName, Path.Combine(fivetoolsDir, "bestiary"), "bestiary-*.json", "monster",
            _ => EntityType.Monster);

        // magic/mundane items (classified by rarity)
        LoadGlob(entries, byName, fivetoolsDir, "items.json", "item",
            e => FivetoolsEntityTypeMap.ForItem(
                e.TryGetProperty("rarity", out var r) ? r.GetString() : null));

        // base items — always mundane
        LoadGlob(entries, byName, fivetoolsDir, "items-base.json", "baseitem",
            _ => EntityType.Item);

        // remaining top-level entity files (single-file each)
        LoadGlob(entries, byName, fivetoolsDir, "backgrounds.json", "background", _ => EntityType.Background);
        LoadGlob(entries, byName, fivetoolsDir, "races.json", "race", _ => EntityType.Race);
        // subraces — the same file's subrace[] array. Loaded AFTER base races so a colliding name
        // resolves to Race first, mirroring the class-before-subclass convention above. Without this
        // the EGW lineages (Draconblood, Ravenite, Pallid, Lotusden) match nothing at all.
        LoadGlob(entries, byName, fivetoolsDir, "races.json", "subrace", _ => EntityType.Subrace);
        // feats: exclude Fighting-Style sub-features (category exactly "FS")
        LoadGlob(entries, byName, fivetoolsDir, "feats.json", "feat", _ => EntityType.Feat,
            include: e => !e.TryGetProperty("category", out var cat) ||
                          (cat.GetString() ?? string.Empty) != "FS");
        LoadGlob(entries, byName, fivetoolsDir, "conditionsdiseases.json", "condition", _ => EntityType.Condition);
        LoadGlob(entries, byName, fivetoolsDir, "deities.json", "deity", _ => EntityType.God);

        Entries = entries;
        EntriesByName = byName.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<(string Canonical, EntityType Type, string Source)>)kv.Value,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Normalises a name for index lookup: lowercase, keep only letters and digits.
    /// </summary>
    public static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // ---------------------------------------------------------------------------

    private static void LoadGlob(
        Dictionary<string, (string Canonical, EntityType Type)> entries,
        Dictionary<string, List<(string Canonical, EntityType Type, string Source)>> byName,
        string directory,
        string pattern,
        string arrayKey,
        Func<JsonElement, EntityType> typeSelector,
        Func<JsonElement, bool>? include = null,
        Func<JsonElement, string?>? aliasSelector = null)
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

                    var type = typeSelector(elem);
                    var source = elem.TryGetProperty("source", out var src)
                        ? src.GetString() ?? string.Empty
                        : string.Empty;
                    Add(entries, byName, name, name, type, source);

                    // Optional alias (e.g. a subclass's shortName) — indexed under its own
                    // normalized key but grounding to the primary entry's canonical name.
                    var alias = aliasSelector?.Invoke(elem);
                    if (!string.IsNullOrEmpty(alias) && alias != name)
                        Add(entries, byName, alias, name, type, source);
                }
            }
        }
    }

    private static void Add(
        Dictionary<string, (string Canonical, EntityType Type)> entries,
        Dictionary<string, List<(string Canonical, EntityType Type, string Source)>> byName,
        string keyName,
        string canonical,
        EntityType type,
        string source)
    {
        var norm = Normalize(keyName);

        // first-wins: keep the entry already in the dictionary if present
        entries.TryAdd(norm, (canonical, type));

        // Multimap: retain every distinct (type, source) a name maps to, first-wins per pair. Keyed
        // on the PAIR rather than the type alone so a cross-SOURCE collision of the same type (the
        // same monster reprinted in two books) is retained and a caller can prefer its own book.
        if (!byName.TryGetValue(norm, out var list))
            byName[norm] = list = new List<(string Canonical, EntityType Type, string Source)>();
        if (list.TrueForAll(e => e.Type != type || !string.Equals(e.Source, source, StringComparison.Ordinal)))
            list.Add((canonical, type, source));
    }
}