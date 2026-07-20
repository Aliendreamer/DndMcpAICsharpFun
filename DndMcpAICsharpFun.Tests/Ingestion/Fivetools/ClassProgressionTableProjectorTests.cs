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
    }
}
