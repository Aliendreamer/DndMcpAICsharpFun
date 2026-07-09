using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

namespace DndMcpAICsharpFun.Features.Encounters;

/// <summary>
/// The encounter-design surface's entry point: rates or builds an encounter for the caller's
/// party, where the party is either an explicit level list or the caller's own campaign's heroes.
/// Campaign access is ownership-checked here (mirrors
/// <see cref="Resolution.CharacterResolutionService.ResolveForUserAsync"/>'s SEC-08 pattern) so a
/// caller can never rate or build against another user's campaign.
/// </summary>
public sealed class EncounterDesignService(
    EncounterAssessor assessor,
    EncounterGenerator generator,
    HeroRepository heroes,
    CampaignRepository campaigns,
    IEntityRetrievalService search)
{
    public async Task<EncounterAssessment> RateForUserAsync(
        long userId,
        long? campaignId,
        IReadOnlyList<int>? partyLevels,
        IReadOnlyList<string> monsters,
        DndVersion ed,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(monsters);

        var party = await ResolvePartyAsync(userId, campaignId, partyLevels, ct);

        var monsterRefs = new List<MonsterRef>(monsters.Count);
        foreach (var monster in monsters)
        {
            monsterRefs.Add(await ResolveMonsterAsync(monster, ct));
        }

        return assessor.Assess(party, monsterRefs, ed);
    }

    public async Task<BuiltEncounter> BuildForUserAsync(
        long userId,
        long? campaignId,
        IReadOnlyList<int>? partyLevels,
        Difficulty target,
        DndVersion ed,
        string? theme,
        CancellationToken ct)
    {
        var party = await ResolvePartyAsync(userId, campaignId, partyLevels, ct);
        return await generator.BuildAsync(party, target, ed, theme, crLte: null, crGte: null, ct);
    }

    /// <summary>
    /// Resolves the party's levels: an explicit <paramref name="partyLevels"/> list always wins;
    /// otherwise the caller's own <paramref name="campaignId"/> is used, ownership-checked so a
    /// mismatched or absent campaign never reveals another user's heroes; otherwise a clear
    /// <see cref="ArgumentException"/> tells the caller to supply one or the other.
    /// </summary>
    private async Task<IReadOnlyList<int>> ResolvePartyAsync(
        long userId, long? campaignId, IReadOnlyList<int>? partyLevels, CancellationToken ct)
    {
        if (partyLevels is { Count: > 0 })
            return partyLevels;

        if (campaignId is { } id)
        {
            var campaign = await campaigns.GetByIdAsync(id, userId);
            if (campaign is null)
                throw new UnauthorizedAccessException("Campaign not found or not owned by the caller.");

            var partyHeroes = await heroes.GetByCampaignAsync(id);

            // A hero with no snapshot at all (never actually observed in practice — creation
            // always writes an initial snapshot) defaults to level 1 rather than being skipped,
            // so a mid-party gap can't silently shrink the party size used for XP budgeting.
            var levels = partyHeroes.Select(h => h.LatestSnapshot?.Level ?? 1).ToList();

            // An owned campaign with zero heroes is not a valid party. Left unchecked, the pure
            // math downstream (EncounterMath.PartyBudget([]) is all-zero, and Classify's >=
            // comparisons against a zero budget) would rate EVERY encounter as Deadly/2014 or
            // Hard/2024 — a confidently-wrong rating instead of the explicit "missing party" error
            // this should be. This mirrors the neither-campaignId-nor-partyLevels case below.
            if (levels.Count == 0)
                throw new ArgumentException("Campaign has no heroes; supply partyLevels or add heroes to the campaign.");

            return levels;
        }

        throw new ArgumentException("supply campaignId or partyLevels");
    }

    /// <summary>
    /// Resolves a caller-supplied monster string — an entity id or a free-text name — to a
    /// <see cref="MonsterRef"/>: id lookup first, falling back to a top-1 entity search by name.
    /// </summary>
    private async Task<MonsterRef> ResolveMonsterAsync(string monster, CancellationToken ct)
    {
        var byId = await search.GetByIdAsync(monster, ct);
        if (byId is not null && MonsterCr.TryRead(byId.Envelope.Fields, out var crById))
        {
            try
            {
                return new MonsterRef(byId.Envelope.Id, byId.Envelope.Name, crById, EncounterMath.CrToXp(crById));
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentException($"Monster not found or has no usable CR: {monster}", nameof(monster));
            }
        }

        var query = new EntitySearchQuery(
            QueryText: monster,
            Type: EntityType.Monster,
            SourceBook: null,
            Edition: null,
            BookType: null,
            SettingTag: null,
            Keyword: null,
            CrNumericLte: null,
            CrNumericGte: null,
            SpellLevel: null,
            DamageType: null,
            TopK: 1);

        var hits = await search.SearchDiagnosticAsync(query, ct);
        var hit = hits.FirstOrDefault();
        if (hit is null || !MonsterCr.TryRead(hit.Fields, out var cr))
            throw new ArgumentException($"Monster not found or has no usable CR: {monster}", nameof(monster));

        try
        {
            return new MonsterRef(hit.Id, hit.Name, cr, EncounterMath.CrToXp(cr));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentException($"Monster not found or has no usable CR: {monster}", nameof(monster));
        }
    }
}