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
    // NOTE: verify these strings equal the real dnd_blocks `source_book` values (see plan Task 6/7).
    private static readonly string[] Core = ["PHB", "DMG", "MM"];

    private static readonly IReadOnlyDictionary<string, string[]> SettingBooks =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Eberron"] = ["ERLW"],
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
