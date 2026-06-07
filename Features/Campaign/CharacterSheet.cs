namespace DndMcpAICsharpFun.Features.Campaign;

public sealed class CharacterSheet
{
    public string Race { get; set; } = "";
    public string Class { get; set; } = "";
    public string Subclass { get; set; } = "";
    public string Background { get; set; } = "";
    public int Level { get; set; }
    public string Alignment { get; set; } = "";
    public int ExperiencePoints { get; set; }

    public int Strength { get; set; }
    public int Dexterity { get; set; }
    public int Constitution { get; set; }
    public int Intelligence { get; set; }
    public int Wisdom { get; set; }
    public int Charisma { get; set; }

    public int MaxHitPoints { get; set; }
    public int CurrentHitPoints { get; set; }
    public int ArmorClass { get; set; }
    public int Speed { get; set; }
    public int Initiative { get; set; }
    public int ProficiencyBonus { get; set; }

    public string SpellcastingAbility { get; set; } = "";
    public int SpellSaveDC { get; set; }
    public int SpellAttackBonus { get; set; }
    public int[] SpellSlots { get; set; } = new int[9];
    public int[] UsedSpellSlots { get; set; } = new int[9];
    public List<string> SpellsKnown { get; set; } = [];

    public List<string> ArmorProficiencies { get; set; } = [];
    public List<string> WeaponProficiencies { get; set; } = [];
    public List<string> ToolProficiencies { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public List<string> SkillProficiencies { get; set; } = [];

    public List<CharacterFeature> Features { get; set; } = [];
    public List<string> Equipment { get; set; } = [];

    public static int Modifier(int score) => (int)Math.Floor((score - 10) / 2.0);
    public static string ModifierStr(int score)
    {
        var m = Modifier(score);
        return m >= 0 ? $"+{m}" : $"{m}";
    }
}

public sealed class CharacterFeature
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
