namespace DndMcpAICsharpFun.Features.Lore;

/// <summary>
/// Maps a named campaign setting to the set of source books its lore lives in, always unioned with
/// the core rulebooks (generic rules apply in every world). The VALUES are stable `dnd_blocks.source_key`
/// payloads (see <c>BookCatalog</c>), not display names — a mismatch silently scopes to nothing. A
/// null/blank/unknown setting resolves to the EMPTY set, meaning "no source-book restriction" (unscoped,
/// today's behavior). In-code registry (like FivetoolsSourceRegistry); grows as setting books are ingested.
/// </summary>
public static class SettingCatalog
{
    private static readonly string[] Core = ["PHB", "DMG", "MM"];

    private static readonly IReadOnlyDictionary<string, string[]> SettingBooks =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Eberron"] = ["ERLW"],
        };

    public static IReadOnlyList<string> KnownSettings => SettingBooks.Keys.ToList();

    /// <summary>Every key the setting-lore scope can ever resolve to: Core ∪ all per-setting book
    /// keys. Used by startup health checks to enumerate every setting-scoped key, independent of
    /// which setting a given campaign picks.</summary>
    public static IReadOnlySet<string> AllScopeKeys { get; } =
        new HashSet<string>(Core.Concat(SettingBooks.Values.SelectMany(v => v)), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> Resolve(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting) || !SettingBooks.TryGetValue(setting, out var books))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase); // unscoped
        }

        return new HashSet<string>(books.Concat(Core), StringComparer.OrdinalIgnoreCase);
    }
}
