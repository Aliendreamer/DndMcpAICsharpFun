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
        new("ERLW", "Eberron: Rising from the Last War",  DndVersion.Edition2014, "ERLW"),
        new("SCAG", "Sword Coast Adventurer's Guide",     DndVersion.Edition2014, "SCAG"),
        new("MTF",  "Mordenkainen's Tome of Foes",        DndVersion.Edition2014, "MTF"),
        new("MPMM", "Mordenkainen Presents: Monsters of the Multiverse", DndVersion.Edition2014, "MPMM"),
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