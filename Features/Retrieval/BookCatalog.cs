using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Retrieval;

/// <summary>Single source of truth for the known source books: stable 5etools Key ↔ exact
/// dnd_blocks.source_book DisplayName. Display names are copied VERBATIM from the live corpus.</summary>
/// <param name="IsCore">Whether this is one of its edition's three core rulebooks. Drives the
/// core-only default allowlist a new campaign receives, so that default is derived from this catalog
/// rather than a second hardcoded list that could drift from it.</param>
/// <param name="PageOffset">How far the stored page (a PDF page) lies past the printed one, measured against
/// 5etools' page numbers; null when it is not known.</param>
public sealed record BookInfo(
    string Key,
    string DisplayName,
    DndVersion Version,
    string FivetoolsSourceKey,
    bool IsCore = false,
    int? PageOffset = null);

public static class BookCatalog
{
    public static IReadOnlyList<BookInfo> All { get; } =
    [
        new("PHB",  "PlayerHandbook 2014",                DndVersion.Edition2014, "PHB", IsCore: true, PageOffset: 1),
        new("MM",   "Monster Manual 2014",                DndVersion.Edition2014, "MM", IsCore: true, PageOffset: 1),
        new("DMG",  "Dungeon Master's Guide 2014",        DndVersion.Edition2014, "DMG", IsCore: true, PageOffset: 0),
        new("XGE",  "Xanathar's Guide to Everything",     DndVersion.Edition2014, "XGE", PageOffset: 1),
        new("TCE",  "Tasha's Cauldron of Everything",     DndVersion.Edition2014, "TCE", PageOffset: 1),
        new("ERLW", "Eberron: Rising from the Last War",  DndVersion.Edition2014, "ERLW", PageOffset: 1),
        new("VRGR", "Van Richten's Guide to Ravenloft",   DndVersion.Edition2014, "VRGR", PageOffset: 1),
        new("FTD",  "Fizban's Treasury of Dragons",        DndVersion.Edition2014, "FTD", PageOffset: 2),
        new("BGG",  "Bigby Presents: Glory of the Giants", DndVersion.Edition2014, "BGG", PageOffset: 0),
        new("EGW",  "Explorer's Guide to Wildemount",      DndVersion.Edition2014, "EGW", PageOffset: 1),
        new("SCC",  "Strixhaven: A Curriculum of Chaos",   DndVersion.Edition2014, "SCC", PageOffset: 1),
        new("GGR",  "Guildmasters' Guide to Ravnica",       DndVersion.Edition2014, "GGR", PageOffset: 1),
        new("SCAG", "Sword Coast Adventurer's Guide",     DndVersion.Edition2014, "SCAG", PageOffset: 1),
        new("MTF",  "Mordenkainen's Tome of Foes",        DndVersion.Edition2014, "MTF", PageOffset: 1),
        new("MPMM", "Mordenkainen Presents: Monsters of the Multiverse", DndVersion.Edition2014, "MPMM", PageOffset: 0),
        // ── Revised core (2024/25). Registered here BEFORE the PDFs are ingested so the startup
        // catalog-drift guard recognises their source_key the moment blocks land — the gap that hid
        // TCE's 2445 blocks from every key-scoped feature. Display names deliberately match the
        // EntityIdSlug override table ("Player's Handbook 2024" -> phb24) so the canonical file and
        // the block payload agree; pass these EXACT strings as displayName when registering.
        new("XPHB", "Player's Handbook 2024",                DndVersion.Edition2024, "XPHB", IsCore: true),
        new("XDMG", "Dungeon Master's Guide 2024",           DndVersion.Edition2024, "XDMG", IsCore: true),
        new("XMM",  "Monster Manual 2025",                   DndVersion.Edition2024, "XMM", IsCore: true),
        // Registered before fivetoolsSourceKey was supplied, so its blocks carry NO source_key at all
        // and its entity ids are slugged from the display name. The key here is 5etools' own EEPC
        // (used by races.json/feats.json/spells-xge.json) purely so block scoping and the drift guard
        // can see it; IngestionRecord.FivetoolsSourceKey is deliberately NOT set, because that would
        // re-slug its entity ids from elemental-evil-player-s-companion.* to eepc.*.
        new("EEPC", "Elemental Evil Player's Companion", DndVersion.Edition2014, "EEPC", PageOffset: 0),
    ];

    public static IReadOnlySet<string> Keys { get; } = All.Select(b => b.Key).ToHashSet(StringComparer.Ordinal);
    public static IReadOnlySet<string> DisplayNames { get; } = All.Select(b => b.DisplayName).ToHashSet(StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, string> DisplayNameToKey { get; } =
        All.ToDictionary(b => b.DisplayName, b => b.Key, StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, string> KeyToDisplayName { get; } =
        All.ToDictionary(b => b.Key, b => b.DisplayName, StringComparer.Ordinal);

    /// <summary>
    /// The catalog key for a book the model or user names: a key in any case ("mtf") or a book name, compared
    /// on its letters with plural/possessive "s" dropped, so "Player's Handbook" finds "PlayerHandbook 2014".
    /// Null for an empty or unknown book.
    /// </summary>
    public static string? ResolveKey(string? book)
    {
        if (string.IsNullOrWhiteSpace(book)) return null;
        var key = Keys.FirstOrDefault(k => string.Equals(k, book.Trim(), StringComparison.OrdinalIgnoreCase));
        if (key is not null) return key;
        var name = NameForm(book);
        return All.FirstOrDefault(b => NameForm(b.DisplayName) == name)?.Key;
    }


    /// <summary>The book's printed title for a citation, from a key or a stored display name;
    /// the input itself when the book is unknown. Without it the model guesses titles from keys
    /// (it cited MTF and MPMM as "Monster Manual").</summary>
    public static string? Title(string? book)
    {
        var key = ResolveKey(book);
        if (key is null) return book;
        var name = KeyToDisplayName[key].Replace("PlayerHandbook", "Player's Handbook");
        return name.EndsWith(" 2014", StringComparison.Ordinal) ? name[..^5] : name;
    }

    private static string NameForm(string name) =>
        string.Concat(System.Text.RegularExpressions.Regex.Matches(name.ToLowerInvariant(), "[a-z]+")
            .Select(m => m.Value.TrimEnd('s')));

    /// <summary>Maps stable source keys to their display names for citation/presentation. An unknown
    /// key passes through unchanged (defensive — a scoped key should always be a catalog member).</summary>
    public static IReadOnlyList<string> ToDisplayNames(IEnumerable<string> keys) =>
        keys.Select(k => KeyToDisplayName.TryGetValue(k, out var name) ? name : k).ToList();

    private static readonly Dictionary<string, int?> PageOffsets =
        All.SelectMany(b => new[] { (Name: b.Key, b.PageOffset), (Name: b.DisplayName, b.PageOffset) })
            .ToDictionary(x => x.Name, x => x.PageOffset, StringComparer.Ordinal);

    /// <summary>The page printed in the book for a stored page; null when the book's offset is not known.</summary>
    /// <param name="book">A book's key (entities) or display name (prose blocks).</param>
    public static int? PrintedPage(string book, int? storedPage) =>
        PageOffsets.GetValueOrDefault(book) is { } offset && storedPage - offset is > 0 and var page ? page : null;
}