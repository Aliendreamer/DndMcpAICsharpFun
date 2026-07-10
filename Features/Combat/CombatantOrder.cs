namespace DndMcpAICsharpFun.Features.Combat;

/// <summary>
/// Deterministic initiative ordering shared by the repository (turn advancement) and the UI, so the
/// displayed order and the turn marker never disagree. Order: highest <see cref="Combatant.InitiativeRoll"/>
/// first (an unset roll sorts last), then highest <see cref="Combatant.InitiativeModifier"/>, then
/// players before monsters, then lowest <see cref="Combatant.AddedOrder"/> (stable insertion).
/// </summary>
public static class CombatantOrder
{
    public static IReadOnlyList<Combatant> Sort(IEnumerable<Combatant> combatants) =>
        combatants
            .OrderByDescending(c => c.InitiativeRoll.HasValue)
            .ThenByDescending(c => c.InitiativeRoll ?? 0)
            .ThenByDescending(c => c.InitiativeModifier)
            .ThenByDescending(c => c.IsPlayer)
            .ThenBy(c => c.AddedOrder)
            .ThenBy(c => c.Id)
            .ToList();
}
