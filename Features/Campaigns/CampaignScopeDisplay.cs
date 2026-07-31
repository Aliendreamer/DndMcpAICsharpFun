using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Features.Campaigns;

/// <summary>
/// Display formatting for a <see cref="CampaignScope"/>. It lives here, beside the resolver, because
/// BOTH campaign forms render the same summary — the create form and the detail page — and a copy in
/// each razor file would drift. Pure string formatting: the scope itself is decided by
/// <see cref="CampaignScopeResolver"/> and never re-derived here.
/// </summary>
public static class CampaignScopeDisplay
{
    /// <summary>How many mechanics books are named before the rest collapse to a count. A campaign
    /// migrated from the pre-edition schema carries all sixteen books, and naming every one wrapped
    /// the summary to three dense lines — unreadable exactly where the scope matters least.</summary>
    private const int MaxNamed = 3;

    /// <summary>The mechanics scope as display names, capped at <see cref="MaxNamed"/> with a
    /// "+N more" tail. An EMPTY scope renders as "none" rather than an empty string: honest-empty is
    /// a real state (a DM who unchecked every rulebook for the edition) and the caller pairs it with
    /// a warning, so it must read as a value, not as a missing one.</summary>
    public static string Mechanics(CampaignScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.MechanicsScope.Count == 0)
        {
            return "none";
        }

        var names = BookCatalog.ToDisplayNames(scope.MechanicsScope);
        return names.Count <= MaxNamed
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(MaxNamed))} +{names.Count - MaxNamed} more";
    }

    /// <summary>The lore scope as a count, pluralised. Lore is deliberately a COUNT and mechanics a
    /// LIST: the rulebooks are the ones a DM must be able to name (they decide adjudications), while
    /// lore breadth is the only thing the summary needs to convey.</summary>
    public static string Lore(CampaignScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return $"{scope.LoreScope.Count} book{(scope.LoreScope.Count == 1 ? "" : "s")}";
    }
}