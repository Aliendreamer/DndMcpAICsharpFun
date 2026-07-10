using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Dice;
using DndMcpAICsharpFun.Features.Encounters;

namespace DndMcpAICsharpFun.Features.Combat;

/// <summary>
/// Cross-aggregate orchestration for combats: drafting combatants from the party, a built encounter,
/// or a manual entry, and ending a combat (DM-approved HP write-back + campaign-log breadcrumb).
/// The thin data gateway is <see cref="CombatRepository"/>; this composes it with the hero, log, and
/// dice services that a repository should not reach into.
/// </summary>
public sealed class CombatService(
    CombatRepository combats,
    HeroRepository heroes,
    CampaignLogRepository log,
    DiceRoller roller)
{
    private readonly CampaignLogRepository _log = log;

    public int RollInitiative(int modifier) =>
        roller.Roll(new DiceExpression(1, 20, modifier, RollMode.Normal)).Total;

    public async Task DraftPartyAsync(long combatId, long campaignId, long userId)
    {
        var party = await heroes.GetByCampaignAsync(campaignId);
        foreach (var hero in party)
        {
            var sheet = hero.LatestSnapshot?.Sheet;
            await combats.AddCombatantAsync(combatId, campaignId, userId, new Combatant
            {
                HeroId = hero.Id,
                Name = hero.Name,
                IsPlayer = true,
                InitiativeRoll = null,
                MaxHp = sheet?.MaxHitPoints ?? 0,
                CurrentHp = sheet?.CurrentHitPoints ?? 0,
                Ac = sheet?.ArmorClass,
            });
        }
    }

    public async Task DraftMonstersAsync(
        long combatId, long campaignId, long userId, IReadOnlyList<MonsterRef> monsters)
    {
        foreach (var monster in monsters)
        {
            await combats.AddCombatantAsync(combatId, campaignId, userId, new Combatant
            {
                Name = monster.Name,
                IsPlayer = false,
                InitiativeModifier = monster.InitiativeModifier,
                InitiativeRoll = RollInitiative(monster.InitiativeModifier),
                MaxHp = 0,
                CurrentHp = 0,
            });
        }
    }

    public async Task AddManualAsync(
        long combatId, long campaignId, long userId,
        string name, int maxHp, int? ac, bool isPlayer, int initiativeModifier)
    {
        await combats.AddCombatantAsync(combatId, campaignId, userId, new Combatant
        {
            Name = name,
            IsPlayer = isPlayer,
            InitiativeModifier = initiativeModifier,
            InitiativeRoll = isPlayer ? null : RollInitiative(initiativeModifier),
            MaxHp = maxHp,
            CurrentHp = maxHp,
            Ac = ac,
        });
    }

    /// <summary>
    /// Ends a combat: for each player combatant linked to a hero, appends a new <c>HeroSnapshot</c>
    /// that clones the hero's latest sheet with <c>CurrentHitPoints</c> set to the DM-approved value
    /// (falling back to the combatant's tracked HP if not in the map); then marks the combat ended and
    /// drops a Combat breadcrumb in the campaign log. Callers invoke this only after the DM approves
    /// the end-combat review — nothing here re-prompts; the approval gate lives in the UI.
    /// </summary>
    public async Task EndCombatAsync(
        long combatId, long campaignId, long userId, IReadOnlyDictionary<long, int> approvedHpByCombatantId)
    {
        var combat = await combats.GetByIdAsync(combatId, campaignId, userId);
        if (combat is null) return;

        var combatants = await combats.GetCombatantsAsync(combatId, campaignId, userId);

        var partyById = (await heroes.GetByCampaignAsync(campaignId)).ToDictionary(h => h.Id);
        foreach (var c in combatants.Where(c => c is { IsPlayer: true, HeroId: not null }))
        {
            if (!partyById.TryGetValue(c.HeroId!.Value, out var hero)) continue;
            var latest = hero.LatestSnapshot;
            if (latest is null) continue;

            var approvedHp = approvedHpByCombatantId.TryGetValue(c.Id, out var hp) ? hp : c.CurrentHp;
            var sheet = latest.Sheet;
            sheet.CurrentHitPoints = approvedHp;
            await heroes.SaveSnapshotAsync(c.HeroId.Value, latest.SessionNumber, $"Post-combat: {combat.Name}", sheet);
        }

        await combats.EndAsync(combatId, campaignId, userId);

        var payload = new CombatLogPayload(
            combat.Name,
            combat.Edition.ToString(),
            combat.Round,
            CombatantOrder.Sort(combatants)
                .Select(c => new CombatCombatantLog(c.Name, c.IsPlayer, c.InitiativeRoll, c.CurrentHp, c.MaxHp))
                .ToList());
        await _log.AddCombatAsync(userId, campaignId, payload, combat.Name);
    }
}
