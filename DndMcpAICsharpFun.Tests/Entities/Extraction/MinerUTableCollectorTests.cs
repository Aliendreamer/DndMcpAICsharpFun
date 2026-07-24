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

    [Fact]
    public void Uncaptioned_table_takes_preceding_section_header_name()
    {
        var html = "<table><tr><td>Black</td><td>Acid</td></tr><tr><td>Blue</td><td>Lightning</td></tr></table>";
        var doc = new PdfStructureDocument("md", new List<PdfStructureItem>
        {
            new("section_header", "Draconic Ancestry", 34, 2),
            new("text", "Your draconic ancestry determines...", 34, null),
            new("table", "", 34, null, html),
        });
        var tables = MinerUTableCollector.Collect(doc, "phb14", "PHB");
        tables.Should().ContainSingle();
        tables[0].Name.Should().Be("Draconic Ancestry");
        tables[0].Id.Should().StartWith("phb14.table.draconic-ancestry");
    }

    [Fact]
    public void Captioned_table_keeps_its_caption()
    {
        var html = "<table><tr><td>x</td><td>y</td></tr><tr><td>1</td><td>2</td></tr></table>";
        var doc = new PdfStructureDocument("md", new List<PdfStructureItem>
        {
            new("section_header", "Some Section", 1, 2),
            new("table", "Real Caption", 1, null, html),
        });
        var tables = MinerUTableCollector.Collect(doc, "phb14", "PHB");
        tables[0].Name.Should().Be("Real Caption");
    }

    [Fact]
    public void Uncaptioned_table_with_no_nearby_heading_falls_back_to_positional()
    {
        var html = "<table><tr><td>x</td><td>y</td></tr><tr><td>1</td><td>2</td></tr></table>";
        var items = new List<PdfStructureItem> { new("section_header", "Far Away", 1, 2) };
        for (var i = 0; i < 15; i++) items.Add(new("text", $"para {i}", 1, null)); // push heading out of the window
        items.Add(new("table", "", 1, null, html));
        var tables = MinerUTableCollector.Collect(new PdfStructureDocument("md", items), "phb14", "PHB");
        tables[0].Name.Should().Be("Table 1");
    }
}