using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Retrieval;

/// <summary>Single source of truth for the known source books: stable 5etools Key ↔ exact
/// dnd_blocks.source_book DisplayName. Display names are copied VERBATIM from the live corpus.</summary>
public sealed record BookInfo(string Key, string DisplayName, DndVersion Version, string FivetoolsSourceKey);

public static class BookCatalog
{
    public static IReadOnlyList<BookInfo> All { get; } =
    [
        new("PHB",  "PlayerHandbook 2014",                DndVersion.Edition2014, "PHB"),
        new("MM",   "Monster Manual 2014",                DndVersion.Edition2014, "MM"),
        new("DMG",  "Dungeon Master's Guide 2014",        DndVersion.Edition2014, "DMG"),
        new("XGE",  "Xanathar's Guide to Everything",     DndVersion.Edition2014, "XGE"),
        new("TCE",  "Tasha's Cauldron of Everything",     DndVersion.Edition2014, "TCE"),
        new("ERLW", "Eberron: Rising from the Last War",  DndVersion.Edition2014, "ERLW"),
        new("VRGR", "Van Richten's Guide to Ravenloft",   DndVersion.Edition2014, "VRGR"),
        new("FTD",  "Fizban's Treasury of Dragons",        DndVersion.Edition2014, "FTD"),
        new("BGG",  "Bigby Presents: Glory of the Giants", DndVersion.Edition2014, "BGG"),
        new("EGW",  "Explorer's Guide to Wildemount",      DndVersion.Edition2014, "EGW"),
        new("SCC",  "Strixhaven: A Curriculum of Chaos",   DndVersion.Edition2014, "SCC"),
        new("GGR",  "Guildmasters' Guide to Ravnica",       DndVersion.Edition2014, "GGR"),
        new("SCAG", "Sword Coast Adventurer's Guide",     DndVersion.Edition2014, "SCAG"),
        new("MTF",  "Mordenkainen's Tome of Foes",        DndVersion.Edition2014, "MTF"),
        new("MPMM", "Mordenkainen Presents: Monsters of the Multiverse", DndVersion.Edition2014, "MPMM"),
        // ── Revised core (2024/25). Registered here BEFORE the PDFs are ingested so the startup
        // catalog-drift guard recognises their source_key the moment blocks land — the gap that hid
        // TCE's 2445 blocks from every key-scoped feature. Display names deliberately match the
        // EntityIdSlug override table ("Player's Handbook 2024" -> phb24) so the canonical file and
        // the block payload agree; pass these EXACT strings as displayName when registering.
        new("XPHB", "Player's Handbook 2024",                DndVersion.Edition2024, "XPHB"),
        new("XDMG", "Dungeon Master's Guide 2024",           DndVersion.Edition2024, "XDMG"),
        new("XMM",  "Monster Manual 2025",                   DndVersion.Edition2024, "XMM"),
        // Registered before fivetoolsSourceKey was supplied, so its blocks carry NO source_key at all
        // and its entity ids are slugged from the display name. The key here is 5etools' own EEPC
        // (used by races.json/feats.json/spells-xge.json) purely so block scoping and the drift guard
        // can see it; IngestionRecord.FivetoolsSourceKey is deliberately NOT set, because that would
        // re-slug its entity ids from elemental-evil-player-s-companion.* to eepc.*.
        new("EEPC", "Elemental Evil Player's Companion", DndVersion.Edition2014, "EEPC"),
    ];

    public static IReadOnlySet<string> Keys { get; } = All.Select(b => b.Key).ToHashSet(StringComparer.Ordinal);
    public static IReadOnlySet<string> DisplayNames { get; } = All.Select(b => b.DisplayName).ToHashSet(StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, string> DisplayNameToKey { get; } =
        All.ToDictionary(b => b.DisplayName, b => b.Key, StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, string> KeyToDisplayName { get; } =
        All.ToDictionary(b => b.Key, b => b.DisplayName, StringComparer.Ordinal);

    /// <summary>Maps stable source keys to their display names for citation/presentation. An unknown
    /// key passes through unchanged (defensive — a scoped key should always be a catalog member).</summary>
    public static IReadOnlyList<string> ToDisplayNames(IEnumerable<string> keys) =>
        keys.Select(k => KeyToDisplayName.TryGetValue(k, out var name) ? name : k).ToList();
}