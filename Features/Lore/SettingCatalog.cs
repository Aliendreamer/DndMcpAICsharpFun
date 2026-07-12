namespace DndMcpAICsharpFun.Features.Lore;

/// <summary>
/// Maps a named campaign setting to the set of source books its lore lives in, always unioned with
/// the core rulebooks (generic rules apply in every world). The KEYS are the exact `source_book`
/// payload values used in `dnd_blocks` — a mismatch silently scopes to nothing. A null/blank/unknown
/// setting resolves to the EMPTY set, meaning "no source-book restriction" (unscoped, today's behavior).
/// In-code registry (like FivetoolsSourceRegistry); grows as setting books are ingested.
/// </summary>
public static class SettingCatalog
{
    // VERIFIED (2026-07-12 live check against dnd_blocks): these are the real
    // `dnd_blocks.source_book` display-name values, not 5etools short keys.
    private static readonly string[] Core = ["PlayerHandbook 2014", "Dungeon Master's Guide 2014", "Monster Manual 2014"];

    private static readonly IReadOnlyDictionary<string, string[]> SettingBooks =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Eberron"] = ["Eberron: Rising from the Last War"],
        };

    public static IReadOnlyList<string> KnownSettings => SettingBooks.Keys.ToList();

    public static IReadOnlySet<string> Resolve(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting) || !SettingBooks.TryGetValue(setting, out var books))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase); // unscoped
        }

        return new HashSet<string>(books.Concat(Core), StringComparer.OrdinalIgnoreCase);
    }
}
