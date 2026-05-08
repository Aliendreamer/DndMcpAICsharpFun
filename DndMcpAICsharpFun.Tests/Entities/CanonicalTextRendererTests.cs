using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class CanonicalTextRendererTests
{
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
    public void Class_renderer_includes_hitdie_proficiencies_and_features()
    {
        var features = new[]
        {
            JsonSerializer.Deserialize<JsonElement>("{\"classFeature\":\"Fighting Style|Fighter||1\",\"level\":1}"),
            JsonSerializer.Deserialize<JsonElement>("{\"classFeature\":\"Second Wind|Fighter||1\",\"level\":1}"),
            JsonSerializer.Deserialize<JsonElement>("{\"classFeature\":\"Action Surge|Fighter||2\",\"level\":2}"),
        };
        var fields = new ClassFields(
            Hd: new HitDice(1, 10),
            Proficiency: new[] { "str", "con" },
            StartingProficiencies: null,
            ClassFeatures: features,
            Multiclassing: null,
            Entries: null,
            SubclassTitle: "Martial Archetype");
        var text = new ClassCanonicalTextRenderer().Render("Fighter", fields);
        text.Should().Contain("d10")
            .And.Contain("STR, CON")
            .And.Contain("Fighting Style (1)")
            .And.Contain("Action Surge (2)");
    }

    [Fact]
    public void Subclass_renderer_includes_classname_and_features()
    {
        var features = new[]
        {
            JsonSerializer.Deserialize<JsonElement>("{\"subclassFeature\":\"Combat Superiority|Fighter|PHB|Battle Master|PHB|3\",\"level\":3}"),
            JsonSerializer.Deserialize<JsonElement>("{\"subclassFeature\":\"Know Your Enemy|Fighter|PHB|Battle Master|PHB|7\",\"level\":7}"),
        };
        var fields = new SubclassFields(
            ClassName: "Fighter",
            ClassSource: "PHB",
            ShortName: "Battle Master",
            SubclassFeatures: features,
            Entries: null);
        var text = new SubclassCanonicalTextRenderer().Render("Battle Master", fields);
        text.Should().Contain("Fighter subclass")
            .And.Contain("Combat Superiority (3)")
            .And.Contain("Know Your Enemy (7)");
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
