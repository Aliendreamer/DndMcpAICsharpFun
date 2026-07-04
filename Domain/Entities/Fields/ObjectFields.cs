using System.Text.Json;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

/// <summary>
/// Structured fields for a D&D <see cref="EntityType.Object"/> — a non-creature that has combat
/// statistics (siege weapons, suspended cauldrons, animated doors/statues). A deliberate subset of
/// <see cref="MonsterFields"/>: objects have Armor Class, Hit Points, immunities, and attack actions,
/// but none of the creature-only fields (ability scores, CR, senses, legendary/lair actions, etc.).
/// Reuses <see cref="MonsterHp"/> and <see cref="MonsterBlock"/> so the extraction decoder handles it
/// with the same machinery as Monster.
/// </summary>
public sealed record ObjectFields(
    IReadOnlyList<JsonElement>? Ac,
    MonsterHp? Hp,
    IReadOnlyList<JsonElement>? Immune,
    IReadOnlyList<JsonElement>? Resist,
    IReadOnlyList<JsonElement>? Vulnerable,
    IReadOnlyList<JsonElement>? ConditionImmune,
    IReadOnlyList<MonsterBlock>? Action,
    string? Description);
