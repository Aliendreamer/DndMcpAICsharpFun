using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class MinerUTableCollectorTests
{
    [Fact]
    public void Collect_parses_table_items_into_canonical_tables_with_provenance()
    {
        var doc = new PdfStructureDocument("", new[]
        {
            new PdfStructureItem("section_header", "Dragonborn", 34, 2),
            new PdfStructureItem("table", "Draconic Ancestry", 34, null,
                Html: "<table><tr><td>Dragon</td><td>Damage Type</td></tr>" +
                      "<tr><td>Black</td><td>Acid</td></tr></table>"),
            new PdfStructureItem("text", "prose about dragonborn", 34, null),
        });

        var tables = MinerUTableCollector.Collect(doc, "phb14", "PHB");

        tables.Should().ContainSingle();
        var t = tables[0];
        t.Name.Should().Be("Draconic Ancestry");
        t.Id.Should().StartWith("phb14.table.draconic-ancestry");
        t.Columns.Should().Equal("Dragon", "Damage Type");
        t.Rows.Should().ContainSingle();
        t.Rows[0].Cells.Select(c => c.Value).Should().Equal("Black", "Acid");
        t.Rows[0].Cells[0].Provenance!.SourceBook.Should().Be("PHB");
        t.Rows[0].Cells[0].Provenance!.Page.Should().Be(34);
    }

    [Fact]
    public void Collect_ignores_non_table_items_and_tables_without_html()
    {
        var doc = new PdfStructureDocument("", new[]
        {
            new PdfStructureItem("text", "prose", 1, null),
            new PdfStructureItem("section_header", "Heading", 1, 2),
            new PdfStructureItem("table", "no-html", 1, null), // Html null → skipped
        });

        MinerUTableCollector.Collect(doc, "phb14", "PHB").Should().BeEmpty();
    }
}
