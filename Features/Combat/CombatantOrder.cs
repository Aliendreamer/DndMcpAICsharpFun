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


    /// <summary>
    /// True when the sort cannot distinguish <paramref name="a"/> and <paramref name="b"/> by anything
    /// above <see cref="Combatant.AddedOrder"/> — equal initiative roll (both unset counts as equal),
    /// modifier, and side. This is exactly the condition under which swapping their AddedOrder reorders
    /// them, so it gates both the manual-reorder swap and the UI's ▲/▼ enable state.
    /// </summary>
    public static bool AreTied(Combatant a, Combatant b) =>
        a.InitiativeRoll == b.InitiativeRoll
        && a.InitiativeModifier == b.InitiativeModifier
        && a.IsPlayer == b.IsPlayer;
}
