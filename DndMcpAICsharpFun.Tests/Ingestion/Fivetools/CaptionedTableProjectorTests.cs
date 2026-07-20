using System.Text.Json;

using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.Fivetools;

public class CaptionedTableProjectorTests
{
    private const string Dragonborn = """
    {"name":"Dragonborn","source":"PHB","entries":[
      {"type":"table","caption":"Draconic Ancestry",
       "colLabels":["Dragon","Damage Type","Breath Weapon"],
       "rows":[["Black","Acid","5 by 30 ft. line (Dex. save)"],["Blue","Lightning","5 by 30 ft. line (Dex. save)"]]}]}
    """;

    [Fact]
    public void Projects_captioned_table_with_sluggable_id()
    {
        using var doc = JsonDocument.Parse(Dragonborn);
        var tables = CaptionedTableProjector.Project(doc.RootElement, "PHB", page: 34);
        tables.Should().ContainSingle();
        var t = tables[0];
        t.Id.Should().Be("phb14.table.draconic-ancestry");
        t.Name.Should().Be("Draconic Ancestry");
        t.Columns.Should().Equal("Dragon", "Damage Type", "Breath Weapon");
        t.Rows.Should().HaveCount(2);
        t.Rows[0].Cells.Select(c => c.Value).Should().Equal("Black", "Acid", "5 by 30 ft. line (Dex. save)");
        t.Rows[0].Cells[0].Provenance!.SourceBook.Should().Be("PHB");
    }

    [Fact]
    public void Skips_uncaptioned_table_blocks()
    {
        using var doc = JsonDocument.Parse("""{"source":"PHB","entries":[{"type":"table","colLabels":["a","b"],"rows":[["1","2"]]}]}""");
        CaptionedTableProjector.Project(doc.RootElement, "PHB", null).Should().BeEmpty();
    }
}