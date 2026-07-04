using System.Text.Json;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

/// <summary>Hit points for an <see cref="EntityType.Object"/> (mirrors MonsterHp but is Object-owned).</summary>
public sealed record ObjectHp(int Average, string? Formula = null);

/// <summary>A named attack action for an <see cref="EntityType.Object"/> (mirrors MonsterBlock).</summary>
public sealed record ObjectAttack(string Name, IReadOnlyList<JsonElement>? Entries = null);

/// <summary>
/// Structured fields for a D&D <see cref="EntityType.Object"/> — a non-creature that has combat
/// statistics (siege weapons, suspended cauldrons, animated doors/statues). A deliberate subset of
/// <see cref="MonsterFields"/>: objects have Armor Class, Hit Points, immunities, and attack actions,
/// but none of the creature-only fields (ability scores, CR, senses, legendary/lair actions, etc.).
/// Uses its OWN <see cref="ObjectHp"/>/<see cref="ObjectAttack"/> sub-types (NOT the shared MonsterHp/
/// MonsterBlock) so the generated schema inlines them with no <c>$ref</c> — a <c>$ref</c> to a shared
/// definition dangles when this schema is embedded as a branch in the extraction discriminated union
/// (Ollama rejects the union with "definitions not in ...").
/// </summary>
public sealed record ObjectFields(
    IReadOnlyList<JsonElement>? Ac,
    ObjectHp? Hp,
    IReadOnlyList<JsonElement>? Immune,
    IReadOnlyList<JsonElement>? Resist,
    IReadOnlyList<JsonElement>? Vulnerable,
    IReadOnlyList<JsonElement>? ConditionImmune,
    IReadOnlyList<ObjectAttack>? Action,
    string? Description);
