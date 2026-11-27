using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public class ClassTablesTests
{
    private const string Barbarian = """
    {"name":"Barbarian","source":"PHB",
     "classTableGroups":[{"colLabels":["Rages","Rage Damage"],"rows":[
       ["2",{"type":"bonus","value":2}],["2",{"type":"bonus","value":2}],["3",{"type":"bonus","value":2}],
       ["3",{"type":"bonus","value":2}],["3",{"type":"bonus","value":2}],["4",{"type":"bonus","value":2}],
       ["4",{"type":"bonus","value":2}],["4",{"type":"bonus","value":2}],["4",{"type":"bonus","value":3}],
       ["4",{"type":"bonus","value":3}],["4",{"type":"bonus","value":3}],["5",{"type":"bonus","value":3}],
       ["5",{"type":"bonus","value":3}],["5",{"type":"bonus","value":3}],["5",{"type":"bonus","value":3}],
       ["5",{"type":"bonus","value":4}],["6",{"type":"bonus","value":4}],["6",{"type":"bonus","value":4}],
       ["6",{"type":"bonus","value":4}],["Unlimited",{"type":"bonus","value":4}]]}],
     "classFeatures":["Rage|Barbarian||1","Brutal Critical (3 dice)|Barbarian||17","Primal Champion|Barbarian||20","Primal Knowledge|Barbarian||3|TCE"]}
    """;

    private const string Warlock = """
    {"name":"Warlock","source":"PHB",
     "classTableGroups":[{"colLabels":["Spell Slots","{@filter Slot Level|spells|class=Warlock}"],"rows":[[1,"1st"]]}],
     "classFeatures":["Mystic Arcanum (8th level)|Warlock||15"]}
    """;

    private const string Wizard = """
    {"name":"Wizard","source":"PHB","classFeatures":[],
     "classTableGroups":[{"title":"Spell Slots per Spell Level",
       "colLabels":["{@filter 1st|spells|level=1|class=Wizard}","{@filter 2nd|spells|level=2|class=Wizard}"],
       "rowsSpellProgression":[[2,0],[3,0],[4,2]]}]}
    """;

    private static IReadOnlyList<string> Lines(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ClassProgressionTableProjector.LevelLines(doc.RootElement);
    }

    [Fact]
    public void Every_level_names_each_value()
    {
        var lines = Lines(Barbarian);
        lines.Should().HaveCount(20);
        lines[16].Should().Be("Level 17: proficiency bonus +6; features: Brutal Critical (3 dice); Rages 6; Rage Damage +4");
        lines[0].Should().Be("Level 1: proficiency bonus +2; features: Rage; Rages 2; Rage Damage +2");
        lines[2].Should().NotContain("Primal Knowledge", "another book's option is not in the printed table");
    }

    [Fact]
    public void Spell_slots_name_their_level_and_empty_cells_are_left_out()
    {
        var lines = Lines(Wizard);
        lines[0].Should().Be("Level 1: proficiency bonus +2; spell slots: 1st-level 2");
        lines[2].Should().Be("Level 3: proficiency bonus +2; spell slots: 1st-level 4, 2nd-level 2");
        lines[3].Should().Be("Level 4: proficiency bonus +2");
    }

    [Fact]
    public void Warlock_features_and_slot_columns_read_plainly()
    {
        var lines = Lines(Warlock);
        lines[0].Should().Be("Level 1: proficiency bonus +2; Spell Slots 1; Slot Level 1st");
        lines[14].Should().Contain("features: Mystic Arcanum (8th level)");
    }

    private static readonly string Printed = string.Join("\n",
        "with peril: rival tribes, deadly weather, and terrifying",
        "The BArBArian",
        "Proficiency Rage",
        "Level Bonus Features Rages Damage",
        "1st +2 Rage, Unarmored 2 +2",
        "Defense",
        "17th +6 Brutal Critical 6 +4",
        "(3 dice)",
        "20th +6 Primal Champion Unlimited +4",
        "monsters. Barbarians charge headlong into that danger");

    [Fact]
    public void The_printed_table_is_swapped_for_the_named_lines_and_the_prose_around_it_stays()
    {
        var text = ClassTables.Relabel(Printed, "Barbarian", Lines(Barbarian));

        text.Should().StartWith("with peril: rival tribes, deadly weather, and terrifying\nThe Barbarian table, by level:\nLevel 1:");
        text.Should().Contain("Level 17: proficiency bonus +6; features: Brutal Critical (3 dice); Rages 6; Rage Damage +4");
        text.Should().EndWith("Level 20: proficiency bonus +6; features: Primal Champion; Rages Unlimited; Rage Damage +4\nmonsters. Barbarians charge headlong into that danger");
        text.Should().NotContain("17th +6").And.NotContain("Proficiency Rage");
    }

    [Fact]
    public void A_table_run_together_on_one_line_is_swapped_too()
    {
        var text = ClassTables.Relabel(Printed.Replace('\n', ' '), "Barbarian", Lines(Barbarian));

        text.Should().StartWith("with peril: rival tribes, deadly weather, and terrifying\nThe Barbarian table, by level:\n");
        text.Should().EndWith("Rages Unlimited; Rage Damage +4\nmonsters. Barbarians charge headlong into that danger");
        text.Should().NotContain("17th +6");
    }

    [Fact]
    public void The_slot_columns_read_apart_as_bare_numbers_are_dropped()
    {
        var text = "acolyte background. Cantrips -Spell Slots per Spell Level- Known 1st 2nd 3rd 3 2 3 3 4 4 3 2 Class Features As a cleric";

        ClassTables.Relabel(text, "Cleric", Lines(Barbarian)).Should().Be("acolyte background. Class Features As a cleric");
    }

    [Fact]
    public void A_text_without_the_whole_table_is_left_alone()
    {
        var piece = "…Rage, Unarmored 2 +2\n17th +6 Brutal Critical 6 +4\n20th +6 Primal Champion Unlimited +4";
        ClassTables.Relabel(piece, "Barbarian", Lines(Barbarian)).Should().Be(piece);
        ClassTables.Relabel("Rage. In battle, you fight with primal ferocity.", "Barbarian", Lines(Barbarian))
            .Should().Be("Rage. In battle, you fight with primal ferocity.");
    }

    [Fact]
    public void Only_a_class_with_known_lines_is_changed()
    {
        var tables = new ClassTables(new Dictionary<string, IReadOnlyList<string>> { ["PHB|barbarian"] = Lines(Barbarian) });
        tables.Relabel(Envelope("Barbarian", "PHB", EntityType.Class), Printed).Should().Contain("Level 17:");
        tables.Relabel(Envelope("Barbarian", "MM", EntityType.Class), Printed).Should().Be(Printed);
        tables.Relabel(Envelope("Barbarian", "PHB", EntityType.Lore), Printed).Should().Be(Printed);
    }

    private static EntityEnvelope Envelope(string name, string book, EntityType type) =>
        new("x", type, name, book, "Edition2014", null, new FirstAppearance(book, "Edition2014"), [], [], Printed, default);
}