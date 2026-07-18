using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class HtmlTableParserTests
{
    private static readonly ProvenanceRef Prov = new("phb14.block.table.0", "PHB", 34);

    // The Draconic Ancestry table as MinerU emits it (td-based, header in the first row).
    private const string DraconicHtml = """
        <table>
          <tr><td>Dragon</td><td>Damage Type</td><td>Breath Weapon</td></tr>
          <tr><td>Black</td><td>Acid</td><td>5 by 30 ft. line (Dex. save)</td></tr>
          <tr><td>Blue</td><td>Lightning</td><td>5 by 30 ft. line (Dex. save)</td></tr>
        </table>
        """;

    [Fact]
    public void Parses_columns_and_rows_from_a_td_table()
    {
        var t = HtmlTableParser.Parse(DraconicHtml, "phb14.table.draconic-ancestry", "Draconic Ancestry", Prov);

        t.Should().NotBeNull();
        t!.Name.Should().Be("Draconic Ancestry");
        t.Columns.Should().Equal("Dragon", "Damage Type", "Breath Weapon");
        t.Rows.Should().HaveCount(2);
        t.Rows[0].Cells.Select(c => c.Value).Should().Equal("Black", "Acid", "5 by 30 ft. line (Dex. save)");
        t.Rows[1].Cells.Select(c => c.Value).Should().Equal("Blue", "Lightning", "5 by 30 ft. line (Dex. save)");
        t.Rows[0].Cells[0].Provenance.Should().Be(Prov); // every cell carries the table's provenance
    }

    [Fact]
    public void Uses_th_header_row_as_columns()
    {
        const string html = "<table><thead><tr><th>A</th><th>B</th></tr></thead>" +
                            "<tbody><tr><td>1</td><td>2</td></tr></tbody></table>";
        var t = HtmlTableParser.Parse(html, "t", "T", Prov);

        t!.Columns.Should().Equal("A", "B");
        t.Rows.Should().ContainSingle();
        t.Rows[0].Cells.Select(c => c.Value).Should().Equal("1", "2");
    }

    [Fact]
    public void Normalizes_whitespace_entities_and_inner_tags()
    {
        const string html = "<table><tr><td>  Cold  &amp;   Fire </td><td><b>Dex.</b>\n save</td></tr>" +
                            "<tr><td>x</td><td>y</td></tr></table>";
        var t = HtmlTableParser.Parse(html, "t", "T", Prov);

        t!.Columns.Should().Equal("Cold & Fire", "Dex. save");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<div>not a table</div>")]
    public void Malformed_or_empty_html_yields_null(string? html) =>
        HtmlTableParser.Parse(html, "t", "T", Prov).Should().BeNull();
}
