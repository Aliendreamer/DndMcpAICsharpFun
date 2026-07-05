using System.Text.Json;
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

    /// <summary>
    /// Captures JSON properties not mapped to a member — notably legacy flat "Class"/"Subclass"/"Level"
    /// on pre-multiclass HeroSnapshot rows. Consumed and cleared by <see cref="OnDeserialized"/>; never
    /// re-serialized (new writes only contain <see cref="Classes"/>).
    /// </summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        // Tolerant migration: a legacy single-class snapshot has flat "Class"/"Level" (captured in Extra)
        // and no "Classes". Back-fill a one-entry list. Only fires when Classes is empty AND a flat class
        // name is present, so a genuinely class-less sheet stays empty.
        if (Classes.Count == 0 && Extra is not null
            && Extra.TryGetValue("Class", out var c) && c.ValueKind == JsonValueKind.String)
        {
            var cls = c.GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(cls))
            {
                var sub = Extra.TryGetValue("Subclass", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString() ?? "" : "";
                var lvl = Extra.TryGetValue("Level", out var l) && l.ValueKind == JsonValueKind.Number
                    ? l.GetInt32() : 1;
                Classes.Add(new ClassLevel { Class = cls, Subclass = sub, Level = lvl });
            }
        }
        Extra = null; // do not echo legacy keys back out on the next serialize
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
