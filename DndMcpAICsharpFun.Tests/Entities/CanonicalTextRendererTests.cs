using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
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
    public void Object_renderer_includes_ac_hp_immunities_and_attack_action()
    {
        var fields = new ObjectFields(
            Ac: new[] { JsonSerializer.Deserialize<JsonElement>("15") },
            Hp: new ObjectHp(50, "unbroken"),
            Immune: new[]
            {
                JsonSerializer.Deserialize<JsonElement>("\"poison\""),
                JsonSerializer.Deserialize<JsonElement>("\"psychic\""),
            },
            Resist: null,
            Vulnerable: null,
            ConditionImmune: null,
            Action: new[]
            {
                new ObjectAttack("Bolt", new[]
                {
                    JsonSerializer.Deserialize<JsonElement>("\"Ranged Weapon Attack: +6 to hit. Hit: 3d10 piercing damage.\""),
                }),
            },
            Description: "A Large object.");
        var text = new ObjectCanonicalTextRenderer().Render("Ballista", fields);
        text.Should().Contain("AC 15")
            .And.Contain("HP 50 (unbroken)")
            .And.Contain("Damage Immunities: poison, psychic")
            .And.Contain("Bolt: Ranged Weapon Attack: +6 to hit. Hit: 3d10 piercing damage.");
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

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static EntityEnvelope BuildEnvelope(EntityType type, string name, JsonElement fields) =>
        new(
            Id: $"test.{type}.{name}", Type: type, Name: name,
            SourceBook: "TEST", Edition: "Edition2014", Page: null,
            FirstAppearedIn: new FirstAppearance("TEST", "Edition2014", null),
            RevisedIn: Array.Empty<Revision>(), SettingTags: Array.Empty<string>(),
            CanonicalText: "", Fields: fields);

    // COR-Sacred-Statue: live corpus ingest observed mtf.monster.sacred-statue /
    // mpmm.monster.sacred-statue SKIPPED because MonsterCanonicalTextRenderer.Render threw.
    // Sacred Statue is a genuinely SPARSE monster record (no ac/hp/speed; cr/type present but
    // their shapes vary across 5etools-derived data). These two lock in the real corpus shapes
    // as a non-throwing regression; the tests below reproduce the *actual* wrong-kind accessor
    // bugs found while auditing the renderer (nested type/cr/ac sub-properties assumed to be a
    // specific ValueKind without checking first) that make such sparse/irregular records throw.

    [Fact]
    public void Monster_renderer_handles_real_sacred_statue_sparse_fields_Mtf()
    {
        var fields = Parse("""
            { "size": ["L"], "type": { "type": "object", "tags": ["construct"] }, "cr": "1/8",
              "keywords": ["Construct"], "traitTags": ["False Appearance"], "senseTags": ["D"],
              "languageTags": ["LF"], "_fivetoolsFilledFields": ["languageTags", "senseTags", "traitTags"] }
            """);
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Monster, "Sacred Statue", fields));
        text.Should().Contain("Sacred Statue");
    }

    [Fact]
    public void Monster_renderer_handles_real_sacred_statue_sparse_fields_Mpmm()
    {
        var fields = Parse("""
            { "size": ["L"], "type": { "type": "construct", "tags": ["statue"] }, "cr": {},
              "ac": [ { "ac": 15, "from": ["construct armor"] } ],
              "hp": { "average": 15, "formula": "2d8+4" },
              "speed": { "walk": 0 },
              "str": 14, "dex": 8, "con": 16, "int": 10, "wis": 14, "cha": 8 }
            """);
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Monster, "Sacred Statue", fields));
        text.Should().Contain("Sacred Statue");
    }

    [Fact]
    public void Monster_renderer_never_throws_when_fields_are_completely_sparse()
    {
        var fields = Parse("{}");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Monster, "Blank Golem", fields));
        text.Should().Contain("Blank Golem");
    }

    [Fact]
    public void Monster_renderer_never_throws_when_nested_type_subproperty_is_wrong_kind()
    {
        // 5etools monster.type is usually {"type": "<string>", "tags": [...]} (or a plain string),
        // but malformed/LLM-extracted canonical JSON can carry a wrong-kind nested "type" value.
        var fields = Parse("""{ "size": ["L"], "type": { "type": 42, "tags": [] }, "cr": "1/8" }""");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Monster, "Odd Statue", fields));
        text.Should().Contain("Odd Statue");
    }

    [Fact]
    public void Monster_renderer_never_throws_when_nested_cr_subproperty_is_wrong_kind()
    {
        // Nested cr shape {"cr": "1/2", "lair": "1"} is normal; a numeric nested "cr" is not.
        var fields = Parse("""{ "size": ["L"], "type": "construct", "cr": { "cr": 0.125 } }""");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Monster, "Fractional Statue", fields));
        text.Should().Contain("Fractional Statue");
    }

    [Fact]
    public void Monster_renderer_never_throws_when_nested_ac_subproperty_is_wrong_kind()
    {
        // ac[0] = {"ac": <int>, "from": [...]} is normal; a string "ac" sub-value is not.
        var fields = Parse("""{ "size": ["L"], "cr": "1/8", "ac": [ { "ac": "15 (natural armor)" } ] }""");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Monster, "Typo Statue", fields));
        text.Should().Contain("Typo Statue");
    }

    [Fact]
    public void Object_renderer_never_throws_when_fields_are_completely_sparse()
    {
        var fields = Parse("{}");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Object, "Blank Object", fields));
        text.Should().Contain("Blank Object");
    }

    [Fact]
    public void Object_renderer_never_throws_when_nested_ac_subproperty_is_wrong_kind()
    {
        var fields = Parse("""{ "ac": [ { "ac": "15 (natural armor)" } ] }""");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Object, "Typo Object", fields));
        text.Should().Contain("Typo Object");
    }

    [Fact]
    public void Class_renderer_never_throws_when_fields_are_completely_sparse()
    {
        var fields = Parse("{}");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Class, "Blank Class", fields));
        text.Should().Contain("Blank Class");
    }

    [Fact]
    public void Class_renderer_never_throws_when_proficiency_array_contains_null()
    {
        var fields = Parse("""{ "hd": { "number": 1, "faces": 8 }, "proficiency": ["str", null] }""");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Class, "Null Prof Class", fields));
        text.Should().Contain("Null Prof Class").And.Contain("STR");
    }

    [Fact]
    public void Class_renderer_never_throws_when_classFeature_level_is_non_integral()
    {
        var fields = Parse("""
            { "hd": { "number": 1, "faces": 8 },
              "classFeatures": [ { "classFeature": "Weird Feature|Test||1.5", "level": 1.5 } ] }
            """);
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Class, "Odd Level Class", fields));
        text.Should().Contain("Odd Level Class");
    }

    [Fact]
    public void Subclass_renderer_never_throws_when_fields_are_completely_sparse()
    {
        var fields = Parse("""{ "className": "Fighter" }""");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Subclass, "Blank Subclass", fields));
        text.Should().Contain("Blank Subclass");
    }
}