using System.Text.Json;
using System.Text.Json.Serialization;

using DndMcpAICsharpFun.Domain.Entities;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities;

public sealed class CanonicalKnowledgeTests
{
    private static readonly JsonSerializerOptions O =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void Table_round_trips_with_provenance()
    {
        var t = new CanonicalTable(
            "phb14.table.draconic-ancestry", "Draconic Ancestry",
            new[] { "ancestry", "damageType" },
            new[]
            {
                new CanonicalTableRow(new[]
                {
                    new CanonicalCell("Red", new ProvenanceRef("phb14.block.123", "PHB", 34)),
                    new CanonicalCell("fire", new ProvenanceRef("phb14.block.123", "PHB", 34)),
                }),
            });

        var rt = JsonSerializer.Deserialize<CanonicalTable>(JsonSerializer.Serialize(t, O), O)!;
        rt.Id.Should().Be("phb14.table.draconic-ancestry");
        rt.Columns.Should().Equal("ancestry", "damageType");
        rt.Rows[0].Cells[1].Value.Should().Be("fire");
        rt.Rows[0].Cells[1].Provenance!.Page.Should().Be(34);
    }

    [Fact]
    public void ChoiceSet_option_links_table_row()
    {
        var cs = new CanonicalChoiceSet(
            "phb14.choiceset.draconic-ancestry", "Draconic Ancestry",
            new[] { new CanonicalChoiceOption("Red", "phb14.table.draconic-ancestry", 7, null) });
        var rt = JsonSerializer.Deserialize<CanonicalChoiceSet>(JsonSerializer.Serialize(cs, O), O)!;
        rt.Options[0].Key.Should().Be("Red");
        rt.Options[0].TableId.Should().Be("phb14.table.draconic-ancestry");
        rt.Options[0].RowIndex.Should().Be(7);
    }
}