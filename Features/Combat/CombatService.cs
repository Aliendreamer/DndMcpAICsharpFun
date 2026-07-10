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
#pragma warning disable CA1823 // CS9113 — reserved for a follow-up task (EndAsync's campaign-log breadcrumb)
    private readonly CampaignLogRepository _log = log;
#pragma warning restore CA1823

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
                InitiativeModifier = 0,
                InitiativeRoll = RollInitiative(0),
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
}
