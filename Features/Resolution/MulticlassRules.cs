using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Resolution;

/// <summary>
/// Deterministic multiclass rules for ANY class combination (caster or not): the ability-score
/// prerequisites to take a class as a multiclass, and the reduced proficiency subset it grants
/// (PHB "Multiclassing" — Proficiencies). No spellcasting is involved here.
/// </summary>
public static class MulticlassRules
{
    public sealed record PrereqResult(bool Allowed, string Reason);

    // Which ability scores (>=13) a class demands to multiclass into it. A class with alternatives
    // (Fighter: STR or DEX) is a list of any-of groups; each group is a set of all-of requirements.
    private static readonly Dictionary<string, (string Ability, Func<CharacterSheet, int> Score)[][]> Prereqs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Barbarian"] = [[("Strength", s => s.Strength)]],
            ["Bard"]      = [[("Charisma", s => s.Charisma)]],
            ["Cleric"]    = [[("Wisdom", s => s.Wisdom)]],
            ["Druid"]     = [[("Wisdom", s => s.Wisdom)]],
            ["Fighter"]   = [[("Strength", s => s.Strength)], [("Dexterity", s => s.Dexterity)]],
            ["Monk"]      = [[("Dexterity", s => s.Dexterity), ("Wisdom", s => s.Wisdom)]],
            ["Paladin"]   = [[("Strength", s => s.Strength), ("Charisma", s => s.Charisma)]],
            ["Ranger"]    = [[("Dexterity", s => s.Dexterity), ("Wisdom", s => s.Wisdom)]],
            ["Rogue"]     = [[("Dexterity", s => s.Dexterity)]],
            ["Sorcerer"]  = [[("Charisma", s => s.Charisma)]],
            ["Warlock"]   = [[("Charisma", s => s.Charisma)]],
            ["Wizard"]    = [[("Intelligence", s => s.Intelligence)]],
            ["Artificer"] = [[("Intelligence", s => s.Intelligence)]],
        };

    // Reduced proficiency grants when the class is taken as a multiclass (NOT the full first-class set).
    private static readonly Dictionary<string, string[]> ProficiencySubsets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Barbarian"] = ["shields", "simple weapons", "martial weapons"],
            ["Bard"]      = ["light armor", "one skill", "one musical instrument"],
            ["Cleric"]    = ["light armor", "medium armor", "shields"],
            ["Druid"]     = ["light armor", "medium armor", "shields"],
            ["Fighter"]   = ["light armor", "medium armor", "shields", "simple weapons", "martial weapons"],
            ["Monk"]      = ["simple weapons", "shortswords"],
            ["Paladin"]   = ["light armor", "medium armor", "shields", "simple weapons", "martial weapons"],
            ["Ranger"]    = ["light armor", "medium armor", "shields", "simple weapons", "martial weapons", "one skill"],
            ["Rogue"]     = ["light armor", "one skill", "thieves' tools"],
            ["Sorcerer"]  = [],
            ["Warlock"]   = ["light armor", "simple weapons"],
            ["Wizard"]    = [],
            ["Artificer"] = ["light armor", "medium armor", "shields", "thieves' tools", "tinker's tools"],
        };

    public static PrereqResult CanMulticlassInto(string @class, CharacterSheet sheet)
    {
        if (!Prereqs.TryGetValue(@class, out var groups))
            return new PrereqResult(false, $"Unknown class '{@class}'.");

        // Allowed if ANY group is fully satisfied (each group is an all-of set of ability >= 13).
        foreach (var group in groups)
            if (group.All(req => req.Score(sheet) >= 13))
                return new PrereqResult(true, "");

        // Build a reason from the option groups (e.g. "Strength 13 or Dexterity 13").
        var options = groups.Select(g => string.Join(" and ", g.Select(r => $"{r.Ability} 13")));
        return new PrereqResult(false, $"Requires {string.Join(" or ", options)}.");
    }

    public static IReadOnlyList<string> MulticlassProficiencies(string @class) =>
        ProficiencySubsets.TryGetValue(@class, out var p) ? p : [];
}
