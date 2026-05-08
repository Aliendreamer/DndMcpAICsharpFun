using System.Text.Json.Serialization;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

// TODO(5etools-alignment): ClassFields record below is unused (old schema shape: HitDie/FeaturesByLevel).
// ClassLevelEntry and FeatureRef are still referenced by SubclassFields.

public sealed record SkillChoice(int Count, IReadOnlyList<string> Options);

public sealed record EquipmentChoice(int Choose, IReadOnlyList<string> Options);

public sealed record MulticlassPrerequisites(
    [property: JsonPropertyName("operator")] string Operator,
    IReadOnlyDictionary<string, int> Abilities);

public sealed record MulticlassBlock(
    MulticlassPrerequisites Prerequisites,
    IReadOnlyList<string> ProficienciesGained);

public sealed record FeatureRef(string Name, string Ref, string Summary);

public sealed record ClassLevelEntry(
    int Level,
    int ProficiencyBonus,
    IReadOnlyList<FeatureRef> Features);

public sealed record ClassFields(
    string HitDie,
    IReadOnlyList<string> PrimaryAbilities,
    IReadOnlyList<string> SavingThrowProficiencies,
    IReadOnlyList<string> ArmorProficiencies,
    IReadOnlyList<string> WeaponProficiencies,
    IReadOnlyList<string> ToolProficiencies,
    SkillChoice SkillChoices,
    IReadOnlyList<EquipmentChoice> StartingEquipment,
    MulticlassBlock Multiclass,
    SpellcastingBlock? Spellcasting,
    int SubclassSelectionLevel,
    IReadOnlyList<string> Subclasses,
    IReadOnlyList<int> AsiLevels,
    IReadOnlyList<ClassLevelEntry> FeaturesByLevel);
