using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.Fivetools;

public class ClassProgressionTableProjectorTests
{
    private const string Fighter = """
    {"name":"Fighter","source":"PHB","hd":{"number":1,"faces":10},
     "classFeatures":["Fighting Style|Fighter||1","Second Wind|Fighter||1","Action Surge|Fighter||2",
                      {"classFeature":"Martial Archetype|Fighter||3","gainSubclassFeature":true},
                      "Ability Score Improvement|Fighter||4","Extra Attack|Fighter||5"]}
    """;

    [Fact]
    public void Martial_class_base_columns_and_prof_bonus()
    {
        using var doc = JsonDocument.Parse(Fighter);
        var t = ClassProgressionTableProjector.Project(doc.RootElement, "PHB");
        t.Id.Should().Be("phb14.table.fighter");
        t.Name.Should().Be("Fighter");
        t.Columns.Take(3).Should().Equal("Level", "Proficiency Bonus", "Features");
        t.Rows.Should().HaveCount(20);
        t.Rows[0].Cells[0].Value.Should().Be("1");
        t.Rows[0].Cells[1].Value.Should().Be("+2");
        t.Rows[0].Cells[2].Value.Should().Be("Fighting Style, Second Wind");
        t.Rows[4].Cells[1].Value.Should().Be("+3"); // level 5
        t.Rows[4].Cells[2].Value.Should().Be("Extra Attack");
        t.Rows[2].Cells[2].Value.Should().Be("Martial Archetype"); // level 3, object-form classFeature
        t.Rows[5].Cells[2].Value.Should().Be(""); // level 6 has no features
    }

    private const string Wizard = """
    {"name":"Wizard","source":"PHB","hd":{"number":1,"faces":6},
     "classFeatures":["Spellcasting|Wizard||1","Arcane Recovery|Wizard||1"],
     "classTableGroups":[
       {"colLabels":["{@filter Cantrips Known|spells|level=0|class=Wizard}"],
        "rows":[[3],[3],[3],[4],[4]]},
       {"title":"Spell Slots per Spell Level",
        "colLabels":["{@filter 1st|spells|level=1|class=Wizard}","{@filter 2nd|spells|level=2|class=Wizard}"],
        "rowsSpellProgression":[[2,0],[3,0],[4,2],[4,3],[4,3]]}]}
    """;

    [Fact]
    public void Caster_appends_group_columns_stripped_and_expanded()
    {
        using var doc = JsonDocument.Parse(Wizard);
        var t = ClassProgressionTableProjector.Project(doc.RootElement, "PHB");
        t.Columns.Should().Equal("Level", "Proficiency Bonus", "Features", "Cantrips Known", "1st", "2nd");
        // level 1: cantrips 3, 1st-slot 2, 2nd-slot blank
        t.Rows[0].Cells[3].Value.Should().Be("3");
        t.Rows[0].Cells[4].Value.Should().Be("2");
        t.Rows[0].Cells[5].Value.Should().Be("");
        // level 3: 2nd-level slots appear (2)
        t.Rows[2].Cells[5].Value.Should().Be("2");
    }
}
