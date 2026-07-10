namespace DndMcpAICsharpFun.Features.Combat;

/// <summary>
/// The 15 standard D&D conditions. Edition-independent: the 2024 revision changed Exhaustion's
/// mechanics but not the set of conditions, so one enum serves both editions. Tracked as a status
/// label on a combatant; the mechanical effect is not simulated.
/// </summary>
public enum Condition
{
    Blinded,
    Charmed,
    Deafened,
    Frightened,
    Grappled,
    Incapacitated,
    Invisible,
    Paralyzed,
    Petrified,
    Poisoned,
    Prone,
    Restrained,
    Stunned,
    Unconscious,
    Exhaustion,
}
