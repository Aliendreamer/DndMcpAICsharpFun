using System.Text.Json;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record MonsterHp(int Average, string? Formula = null, string? Special = null);

public sealed record MonsterBlock(string Name, IReadOnlyList<JsonElement>? Entries = null);

public sealed record MonsterFields(
    IReadOnlyList<string>? Size,
    JsonElement? Type,
    IReadOnlyList<string>? Alignment,
    IReadOnlyList<JsonElement>? Ac,
    MonsterHp? Hp,
    JsonElement? Speed,
    int? Str, int? Dex, int? Con, int? Int, int? Wis, int? Cha,
    JsonElement? Save,
    JsonElement? Skill,
    IReadOnlyList<JsonElement>? Resist,
    IReadOnlyList<JsonElement>? Immune,
    IReadOnlyList<JsonElement>? Vulnerable,
    IReadOnlyList<JsonElement>? ConditionImmune,
    IReadOnlyList<string>? Senses,
    int? Passive,
    IReadOnlyList<string>? Languages,
    JsonElement? Cr,
    IReadOnlyList<MonsterBlock>? Trait,
    IReadOnlyList<MonsterBlock>? Action,
    IReadOnlyList<MonsterBlock>? Bonus,
    IReadOnlyList<MonsterBlock>? Reaction,
    IReadOnlyList<MonsterBlock>? Legendary,
    IReadOnlyList<string>? LegendaryHeader,
    IReadOnlyList<MonsterBlock>? Lair,
    IReadOnlyList<string>? LairHeader,
    IReadOnlyList<JsonElement>? Spellcasting,
    IReadOnlyList<string>? Environment);