namespace DndMcpAICsharpFun.Features.Resolution;

/// <summary>
/// PHB proficient saving-throw abilities per class. A character is proficient in exactly these two
/// saves, granted by the STARTING class only (5e grants no save proficiency from multiclassing).
/// </summary>
public static class SavingThrowProficiencies
{
    private static readonly Dictionary<string, (string, string)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Barbarian"] = ("Strength", "Constitution"),
        ["Bard"] = ("Dexterity", "Charisma"),
        ["Cleric"] = ("Wisdom", "Charisma"),
        ["Druid"] = ("Intelligence", "Wisdom"),
        ["Fighter"] = ("Strength", "Constitution"),
        ["Monk"] = ("Strength", "Dexterity"),
        ["Paladin"] = ("Wisdom", "Charisma"),
        ["Ranger"] = ("Strength", "Dexterity"),
        ["Rogue"] = ("Dexterity", "Intelligence"),
        ["Sorcerer"] = ("Constitution", "Charisma"),
        ["Warlock"] = ("Wisdom", "Charisma"),
        ["Wizard"] = ("Intelligence", "Wisdom"),
    };

    /// <summary>The two proficient save abilities for a class, or null for an unknown/homebrew class.</summary>
    public static (string, string)? For(string @class) => Map.TryGetValue(@class, out var v) ? v : null;
}