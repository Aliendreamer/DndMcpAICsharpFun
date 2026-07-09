using System.Text.Json;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record RaceFields(
    IReadOnlyList<string>? Size,
    JsonElement? Speed,
    IReadOnlyList<JsonElement>? Ability,
    IReadOnlyList<JsonElement>? LanguageProficiencies,
    IReadOnlyList<JsonElement>? SkillProficiencies,
    IReadOnlyList<JsonElement>? ToolProficiencies,
    IReadOnlyList<JsonElement>? WeaponProficiencies,
    IReadOnlyList<JsonElement>? ArmorProficiencies,
    int? Darkvision,
    IReadOnlyList<string>? TraitTags,
    IReadOnlyList<JsonElement>? AdditionalSpells,
    IReadOnlyList<JsonElement>? Resist,
    IReadOnlyList<JsonElement>? Immune,
    IReadOnlyList<JsonElement>? Entries,
    string? Lineage);