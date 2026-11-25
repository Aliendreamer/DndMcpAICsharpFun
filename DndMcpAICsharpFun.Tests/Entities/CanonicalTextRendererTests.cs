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

    private static string RenderMonster(string json) =>
        new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Monster, "Loup Garou", Parse(json)));

    [Fact]
    public void Monster_text_carries_bonus_actions_reactions_and_legendary_descriptions()
    {
        var text = RenderMonster("""
            { "size": ["M"], "cr": "13",
              "action": [ { "name": "Longsword", "entries": ["Melee Weapon Attack: +9 to hit."] } ],
              "bonus": [ { "name": "Change Shape", "entries": ["The loup garou polymorphs."] } ],
              "reaction": [ { "name": "Parry", "entries": ["It adds 3 to its AC."] } ],
              "legendaryHeader": ["It can take 3 legendary actions."],
              "legendary": [ { "name": "Swipe", "entries": ["It makes one Claws attack."] } ] }
            """);
        text.Should().Contain("Bonus Actions:").And.Contain("Change Shape: The loup garou polymorphs.")
            .And.Contain("Reactions:").And.Contain("Parry: It adds 3 to its AC.")
            .And.Contain("It can take 3 legendary actions.").And.Contain("Swipe: It makes one Claws attack.");
    }

    [Fact]
    public void Monster_text_carries_every_paragraph_and_nested_list_of_an_entry()
    {
        var text = RenderMonster("""
            { "size": ["M"], "cr": "1",
              "action": [ { "name": "Eye Rays", "entries": [ "The beholder shoots rays:",
                { "type": "list", "items": [ { "type": "item", "name": "1. Charm Ray", "entries": ["The target is {@condition charmed}."] }, "2. Plain item" ] },
                "A last paragraph." ] } ] }
            """);
        text.Should().Contain("Eye Rays: The beholder shoots rays: 1. Charm Ray. The target is charmed. 2. Plain item A last paragraph.");
    }

    [Fact]
    public void Monster_text_carries_spellcasting_defences_senses_and_languages()
    {
        var text = RenderMonster("""
            { "size": ["M"], "cr": "5", "passive": 14,
              "save": { "dex": "+5", "wis": "+4" }, "skill": { "perception": "+4" },
              "vulnerable": ["fire"], "immune": ["poison"], "conditionImmune": ["charmed"],
              "resist": [ "cold", { "immune": ["bludgeoning", "piercing"], "note": "from nonmagical attacks", "cond": true } ],
              "senses": ["darkvision 60 ft."], "languages": ["Common", "Giant"],
              "spellcasting": [ { "name": "Spellcasting", "type": "spellcasting", "headerEntries": ["It casts a spell:"],
                "will": ["{@spell light}"], "daily": { "1e": ["{@spell bless}", "{@spell aid}"], "2": ["{@spell fly}"] },
                "spells": { "0": { "spells": ["{@spell mending}"] }, "1": { "slots": 4, "spells": ["{@spell shield}"] } } } ] }
            """);
        text.Should().Contain("Saving Throws: dex +5, wis +4").And.Contain("Skills: perception +4")
            .And.Contain("Damage Vulnerabilities: fire").And.Contain("Damage Immunities: poison")
            .And.Contain("Damage Resistances: cold; bludgeoning, piercing from nonmagical attacks")
            .And.Contain("Condition Immunities: charmed")
            .And.Contain("Senses: darkvision 60 ft., passive Perception 14").And.Contain("Languages: Common, Giant")
            .And.Contain("Spellcasting: It casts a spell: At will: light. 1/day each: bless, aid. 2/day: fly. "
                + "Cantrips: mending. 1st level (4 slots): shield.");
    }

    [Fact]
    public void Monster_text_does_not_repeat_spellcasting_already_printed_as_an_action()
    {
        var text = RenderMonster("""
            { "size": ["M"], "cr": "5",
              "action": [ { "name": "Spellcasting", "entries": ["It casts light."] } ],
              "spellcasting": [ { "name": "Spellcasting", "headerEntries": ["It casts a spell:"], "will": ["{@spell light}"] } ] }
            """);
        text.Should().Contain("Spellcasting: It casts light.").And.NotContain("It casts a spell:");
    }

    [Fact]
    public void Monster_text_keeps_the_old_opening_so_the_embedded_head_is_stable()
    {
        var text = RenderMonster("""
            { "size": ["M"], "cr": "5", "senses": ["darkvision 60 ft."], "save": { "dex": "+5" },
              "trait": [ { "name": "Amphibious", "entries": ["It breathes water."] } ],
              "action": [ { "name": "Bite", "entries": ["It bites."] } ],
              "bonus": [ { "name": "Dash", "entries": ["It dashes."] } ] }
            """);
        var order = new[] { "CR 5", "Traits:", "Actions:", "Bonus Actions:", "Saving Throws:", "Senses:" }.Select(s => text.IndexOf(s, StringComparison.Ordinal));
        order.Should().BeInAscendingOrder().And.NotContain(-1);
    }

    [Fact]
    public void Monster_with_lair_text_lines_is_not_degraded_to_its_name()
    {
        // ggr.monster.golgari-shaman: the extractor writes "lair" as plain strings.
        var text = RenderMonster("""
            { "size": ["M"], "cr": "5", "lair": ["On initiative count 20 the lair acts."],
              "action": [ { "name": "Bite", "entries": ["It bites."] } ] }
            """);
        text.Should().Contain("CR 5").And.Contain("Bite: It bites.").And.Contain("On initiative count 20 the lair acts.");
    }

    [Fact]
    public void Monster_text_carries_variants_and_survives_wrong_kinds()
    {
        var text = RenderMonster("""
            { "size": ["M"], "cr": "5", "save": "none", "skill": [], "resist": [42, {}], "conditionImmune": [null],
              "spellcasting": [ 7, { "name": 3, "daily": [], "spells": { "1": 5 } } ],
              "variant": [ { "type": "variant", "name": "Coven", "entries": ["Three hags share a pool.", { "type": "entries", "name": "Pool", "entries": ["It has 10 points."] } ] } ] }
            """);
        text.Should().Contain("CR 5").And.Contain("Coven: Three hags share a pool. Pool. It has 10 points.");
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


    // COR-render-nodrop: whole-branch follow-up. DeserialiseFields<TFields> calls
    // envelope.Fields.Deserialize<TFields>(JsonOptions) UNGUARDED — a type-boundary mismatch
    // (e.g. JSON null or a wrong-kind value bound to a non-nullable value-type property) throws
    // JsonException *before* the (now-hardened) renderer body ever runs. That throw propagates
    // out of Render() and is caught by EntityIngestionOrchestrator's per-entity try/catch, which
    // SKIPS the entity — silently dropping it from dnd_entities. Render() must degrade to a
    // non-empty, name-bearing canonicalText instead of throwing, for ANY failure mode.

    [Fact]
    public void Monster_renderer_degrades_instead_of_throwing_when_hp_average_fails_to_deserialise_as_null()
    {
        // MonsterHp.Average is a non-nullable int; a JSON null for "average" makes
        // System.Text.Json throw JsonException out of DeserialiseFields<MonsterFields> — this is
        // the actual mechanism that dropped "Sacred Statue" at ingest.
        var fields = Parse("""{ "size": ["L"], "type": "construct", "cr": "1/8", "hp": { "average": null } }""");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Monster, "Sacred Statue", fields));
        text.Should().NotBeNullOrWhiteSpace().And.Contain("Sacred Statue");
    }

    [Fact]
    public void Monster_renderer_degrades_instead_of_throwing_when_hp_average_is_wrong_kind()
    {
        // Same type-boundary failure, different wrong-kind value: a JSON string bound to the
        // non-nullable int Average property.
        var fields = Parse("""{ "size": ["L"], "type": "construct", "cr": "1/8", "hp": { "average": "many" } }""");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Monster, "Wordy Statue", fields));
        text.Should().NotBeNullOrWhiteSpace().And.Contain("Wordy Statue");
    }

    [Fact]
    public void Object_renderer_degrades_instead_of_throwing_when_hp_average_is_wrong_kind()
    {
        // ObjectHp.Average is also a non-nullable int — same DeserialiseFields<ObjectFields>
        // type-boundary throw, proving the dispatcher-level fix is NOT Monster-specific.
        var fields = Parse("""{ "hp": { "average": "many" } }""");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Object, "Wordy Object", fields));
        text.Should().NotBeNullOrWhiteSpace().And.Contain("Wordy Object");
    }

    [Fact]
    public void Class_renderer_degrades_instead_of_throwing_when_hitdice_number_is_wrong_kind()
    {
        // HitDice.Number is a non-nullable int — same DeserialiseFields<ClassFields>
        // type-boundary throw for the Class-typed renderer path.
        var fields = Parse("""{ "hd": { "number": "one", "faces": 8 } }""");
        var text = new EntityCanonicalTextDispatcher().Render(BuildEnvelope(EntityType.Class, "Wordy Class", fields));
        text.Should().NotBeNullOrWhiteSpace().And.Contain("Wordy Class");
    }
}