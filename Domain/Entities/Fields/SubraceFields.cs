using System.Text.Json;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record SubraceFields(
    string? RaceName,
    string? RaceSource,
    IReadOnlyList<JsonElement>? Ability,
    JsonElement? Speed,
    int? Darkvision,
    IReadOnlyList<JsonElement>? SkillProficiencies,
    IReadOnlyList<JsonElement>? LanguageProficiencies,
    IReadOnlyList<JsonElement>? WeaponProficiencies,
    IReadOnlyList<JsonElement>? AdditionalSpells,
    IReadOnlyList<JsonElement>? Resist,
    IReadOnlyList<string>? TraitTags,
    IReadOnlyList<JsonElement>? Entries);
