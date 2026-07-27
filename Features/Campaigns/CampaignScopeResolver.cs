using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Features.Campaigns;

/// <summary>The books a campaign can draw on, split by what edition means for each kind of content.</summary>
/// <param name="Edition">The campaign's rules canon — the authority on a mechanics conflict.</param>
/// <param name="MechanicsScope">Allowed books restricted to the campaign's edition. Classes, spells,
/// feats, monsters, items: content whose meaning CHANGES between canons.</param>
/// <param name="LoreScope">Allowed books irrespective of edition. Settings, factions, places, deities:
/// content that is equally true under either canon.</param>
public sealed record CampaignScope(
    DndVersion Edition,
    IReadOnlySet<string> MechanicsScope,
    IReadOnlySet<string> LoreScope);

/// <summary>
/// The single place a campaign becomes a retrieval scope, so no consumer re-derives the rule.
/// <para>
/// The core distinction is that <b>lore is edition-portable and mechanics are not</b>. Scoping
/// everything by edition would cost a 2024 campaign 2,943 entities and every setting book — thirteen
/// of the corpus's sixteen books are supplements and settings that will never get a 2024 equivalent,
/// so a 2024 Eberron table would be told "not in your edition" about its own setting. The boundary is
/// therefore per CONTENT TYPE, not per book.
/// </para>
/// <para>
/// Pure function over stored campaign state: no database, no language model. The edition is a question
/// the campaign already answers, and must never be delegated to a model.
/// </para>
/// </summary>
public static class CampaignScopeResolver
{
    public static CampaignScope Resolve(Campaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);

        // Unknown keys are dropped rather than propagated: they cannot match anything downstream, and
        // the write path already rejects them, so one here means data predating that validation.
        var allowed = campaign.AllowedBookKeys
            .Where(BookCatalog.Keys.Contains)
            .ToHashSet(StringComparer.Ordinal);

        var mechanics = BookCatalog.All
            .Where(b => b.Version == campaign.Edition && allowed.Contains(b.Key))
            .Select(b => b.Key)
            .ToHashSet(StringComparer.Ordinal);

        return new CampaignScope(campaign.Edition, mechanics, allowed);
    }

    /// <summary>The default allowlist for a NEW campaign: its edition's core rulebooks only, derived
    /// from <see cref="BookCatalog"/> so it cannot drift from the catalog. Supplements are opt-in.
    /// Deliberately NOT applied to existing campaigns — see the migration.</summary>
    public static IReadOnlyList<string> CoreBooksFor(DndVersion edition) =>
        BookCatalog.All
            .Where(b => b.IsCore && b.Version == edition)
            .Select(b => b.Key)
            .ToList();
}