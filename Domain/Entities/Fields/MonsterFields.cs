using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record ArmorClass(int Value, string? Source);

public sealed record HitPoints(int Average, string Dice);

public sealed record AbilityScores(
    int Strength, int Dexterity, int Constitution,
    int Intelligence, int Wisdom, int Charisma);

public sealed record ChallengeRating(string Cr, double CrNumeric, int Xp, int ProficiencyBonus);

public sealed record TraitRef(string Name, string Ref, string Summary);

public sealed record SaveBlock(string Ability, int Dc);

public sealed record AttackRange(int Normal, int? Long);

public sealed record DamagePart(string Dice, int Average, string Type, string? Versatile = null);

public enum ActionType
{
    Multiattack,
    MeleeWeaponAttack,
    RangedWeaponAttack,
    MeleeOrRangedWeaponAttack,
    Save,
    Passive,
    Other
}

public sealed record MonsterAction(
    string Name,
    ActionType Type,
    string Summary,
    int? AttackBonus = null,
    int? Reach = null,
    AttackRange? Range = null,
    string? Targets = null,
    IReadOnlyList<DamagePart>? Damage = null,
    string? Recharge = null,
    SaveBlock? Save = null);

public sealed record LegendaryAction(string Name, int Cost, string Summary);

public sealed record LegendaryBlock(int PerTurn, IReadOnlyList<LegendaryAction> Actions);

public sealed record LairActionEntry(string Summary);

public sealed record RegionalEffect(string Summary);

public sealed record LairBlock(
    int InitiativeCount,
    IReadOnlyList<LairActionEntry> Actions,
    IReadOnlyList<RegionalEffect> RegionalEffects);

public sealed record MonsterFields(
    string Size,
    string Type,
    IReadOnlyList<string> Subtypes,
    string Alignment,
    ArmorClass ArmorClass,
    HitPoints HitPoints,
    IReadOnlyDictionary<string, int> Speed,
    AbilityScores AbilityScores,
    IReadOnlyDictionary<string, int> SavingThrows,
    IReadOnlyDictionary<string, int> Skills,
    IReadOnlyList<string> DamageVulnerabilities,
    IReadOnlyList<string> DamageResistances,
    IReadOnlyList<string> DamageImmunities,
    IReadOnlyList<string> ConditionImmunities,
    IReadOnlyDictionary<string, int> Senses,
    IReadOnlyList<string> Languages,
    ChallengeRating ChallengeRating,
    IReadOnlyList<string> Environment,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<TraitRef> Traits,
    IReadOnlyList<MonsterAction> Actions,
    IReadOnlyList<MonsterAction> BonusActions,
    IReadOnlyList<MonsterAction> Reactions,
    SpellcastingBlock? Spellcasting = null,
    LegendaryBlock? LegendaryActions = null,
    LairBlock? LairActions = null,
    IReadOnlyList<string>? VariantForms = null);
