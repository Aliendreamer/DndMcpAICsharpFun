using System.Text.Json;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record SpellTime(int Number, string Unit, string? Condition = null);
public sealed record SpellDistance(string Type, int? Amount = null);
public sealed record SpellRange(string Type, SpellDistance? Distance = null);
public sealed record SpellComponents(bool V = false, bool S = false, JsonElement? M = null);
public sealed record SpellDurationItem(string Type, JsonElement? Duration = null, bool Concentration = false);

public sealed record SpellFields(
    int Level,
    string School,
    IReadOnlyList<SpellTime>? Time,
    SpellRange? Range,
    SpellComponents? Components,
    IReadOnlyList<SpellDurationItem>? Duration,
    bool Ritual,
    bool Concentration,
    IReadOnlyList<JsonElement>? Entries,
    IReadOnlyList<JsonElement>? EntriesHigherLevel,
    IReadOnlyList<string>? DamageInflict,
    IReadOnlyList<string>? SavingThrow,
    IReadOnlyList<string>? Classes,
    IReadOnlyList<string>? ConditionInflict);