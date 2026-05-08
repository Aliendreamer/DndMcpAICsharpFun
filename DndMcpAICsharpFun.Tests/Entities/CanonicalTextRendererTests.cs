using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class CanonicalTextRendererTests
{
    [Fact]
    public void Class_text_includes_hitdie_and_saves_and_level1_features()
    {
        var fields = new ClassFields(
            HitDie: "d10",
            PrimaryAbilities: new[] { "Strength" },
            SavingThrowProficiencies: new[] { "Strength", "Constitution" },
            ArmorProficiencies: Array.Empty<string>(),
            WeaponProficiencies: Array.Empty<string>(),
            ToolProficiencies: Array.Empty<string>(),
            SkillChoices: new SkillChoice(2, new[] { "Athletics" }),
            StartingEquipment: Array.Empty<EquipmentChoice>(),
            Multiclass: new MulticlassBlock(
                new MulticlassPrerequisites("or", new Dictionary<string, int> { ["Strength"] = 13 }),
                Array.Empty<string>()),
            Spellcasting: null,
            SubclassSelectionLevel: 3,
            Subclasses: Array.Empty<string>(),
            AsiLevels: Array.Empty<int>(),
            FeaturesByLevel: new[] {
                new ClassLevelEntry(1, 2, new[] {
                    new FeatureRef("Fighting Style", "x", "Pick a Fighting Style.")
                })
            });

        var text1 = new ClassCanonicalTextRenderer().Render("Fighter", fields);
        var text2 = new ClassCanonicalTextRenderer().Render("Fighter", fields);
        text1.Should().Be(text2); // determinism
        text1.Should().Contain("Fighter").And.Contain("d10").And.Contain("Strength, Constitution").And.Contain("Fighting Style");
    }

   [Fact]
    public void Spell_text_includes_at_higher_levels()
    {
        var mJson = JsonSerializer.Deserialize<JsonElement>("\"a tiny ball of bat guano and sulfur\"");
        var fields = new SpellFields(
            Level: 3,
            School: "V",
            Time: new[] { new SpellTime(1, "action") },
            Range: new SpellRange("point", new SpellDistance("feet", 150)),
            Components: new SpellComponents(V: true, S: true, M: mJson),
            Duration: new[] { new SpellDurationItem("instant") },
            Ritual: false,
            Concentration: false,
            Entries: new[] { JsonSerializer.Deserialize<JsonElement>("\"A bright streak.\"") },
            EntriesHigherLevel: new[] { JsonSerializer.Deserialize<JsonElement>("\"Damage increases.\"") },
            DamageInflict: new[] { "fire" },
            SavingThrow: null,
            Classes: new[] { "Wizard" },
            ConditionInflict: null);
        var text = new SpellCanonicalTextRenderer().Render("Fireball", fields);
        text.Should().Contain("3rd-level Evocation").And.Contain("Damage increases.");
    }

    [Fact]
    public void Monster_text_includes_name_cr_and_type()
    {
        JsonElement? typeEl = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"humanoid\",\"tags\":[\"bullywug\"]}");
        JsonElement? crEl = JsonSerializer.Deserialize<JsonElement>("\"1/4\"");
        var fields = new MonsterFields(
            Size: new[] { "M" },
            Type: typeEl,
            Alignment: new[] { "N", "E" },
            Ac: new[] { JsonSerializer.Deserialize<JsonElement>("15") },
            Hp: new MonsterHp(11, "2d8+2"),
            Speed: null, Str: 12, Dex: 12, Con: 13, Int: 7, Wis: 10, Cha: 7,
            Save: null, Skill: null, Resist: null, Immune: null, Vulnerable: null,
            ConditionImmune: null, Senses: null, Passive: 12, Languages: new[] { "Bullywug" },
            Cr: crEl,
            Trait: new[] { new MonsterBlock("Amphibious", new[] { JsonSerializer.Deserialize<JsonElement>("\"The bullywug can breathe air and water.\"") }) },
            Action: null, Bonus: null, Reaction: null, Legendary: null, LegendaryHeader: null,
            Lair: null, LairHeader: null, Spellcasting: null, Environment: null);
        var text = new MonsterCanonicalTextRenderer().Render("Bullywug", fields);
        text.Should().Contain("Bullywug").And.Contain("CR 1/4").And.Contain("Medium").And.Contain("Amphibious");
    }
}
