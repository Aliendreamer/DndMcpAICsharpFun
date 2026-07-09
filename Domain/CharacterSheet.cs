using System.Text.Json.Serialization;

namespace DndMcpAICsharpFun.Domain;

public sealed class CharacterSheet : IJsonOnDeserialized
{
    public string Race { get; set; } = "";

    /// <summary>Source of truth for the character's class(es). One entry per class taken.</summary>
    public List<ClassLevel> Classes { get; set; } = [];

    [JsonIgnore] public string Class => Classes.Count > 0 ? Classes[0].Class : "";
    [JsonIgnore] public string Subclass => Classes.Count > 0 ? Classes[0].Subclass : "";
    [JsonIgnore] public int Level => Classes.Sum(c => c.Level);

    public string Background { get; set; } = "";
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
    public static int ProficiencyBonusForLevel(int level) => 2 + (Math.Max(1, level) - 1) / 4;
    [JsonIgnore] public int ProficiencyBonus => ProficiencyBonusForLevel(Level);

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
    public Dictionary<string, string> ResolvedChoices { get; set; } = new();

    public static int Modifier(int score) => (int)Math.Floor((score - 10) / 2.0);
    public static string ModifierStr(int score)
    {
        var m = Modifier(score);
        return m >= 0 ? $"+{m}" : $"{m}";
    }

    // Legacy deserialization sinks: pre-multiclass HeroSnapshot rows wrote flat "Class"/"Subclass"/"Level".
    // The [JsonIgnore] derived getters shadow those keys from [JsonExtensionData], so capture them here.
    // Set-only (no getter) => deserialized but never re-serialized. Consumed in OnDeserialized.
    private string? _legacyClass;
    private string? _legacySubclass;
    private int? _legacyLevel;

    [JsonInclude, JsonPropertyName("Class")]
    internal string LegacyClassSink { set => _legacyClass = value; }
    [JsonInclude, JsonPropertyName("Subclass")]
    internal string LegacySubclassSink { set => _legacySubclass = value; }
    [JsonInclude, JsonPropertyName("Level")]
    internal int LegacyLevelSink { set => _legacyLevel = value; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        // Tolerant migration: legacy single-class snapshots have flat "Class"/"Level" and no "Classes".
        // Back-fill a one-entry list, only when Classes is empty AND a flat class name was present.
        if (Classes.Count == 0 && !string.IsNullOrWhiteSpace(_legacyClass))
            Classes.Add(new ClassLevel
            {
                Class = _legacyClass,
                Subclass = _legacySubclass ?? "",
                Level = _legacyLevel ?? 1,
            });
    }

    /// <summary>Sets the character to a single class (the common non-multiclass path).</summary>
    public void SetSingleClass(string @class, string subclass, int level)
        => Classes = [new ClassLevel { Class = @class, Subclass = subclass, Level = level }];
}

public sealed class CharacterFeature
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class ClassLevel
{
    public string Class { get; set; } = "";
    public int Level { get; set; }
    public string Subclass { get; set; } = "";
}