using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

using FluentAssertions;

using Xunit;

public class FivetoolsJsonTests
{
    [Fact]
    public void Table_id_uses_book_slug_and_name_slug()
    {
        EntityIdSlug.Table("PHB", "Draconic Ancestry").Should().Be("phb14.table.draconic-ancestry");
        EntityIdSlug.Table("XGE", "Sample Table").Should().Be("xge.table.sample-table");
    }

    [Theory]
    [InlineData("{@filter 1st|spells|level=1|class=Wizard}", "1st")]
    [InlineData("{@filter Cantrips Known|spells|level=0|class=Wizard}", "Cantrips Known")]
    [InlineData("{@dice 1d6}", "1d6")]
    [InlineData("Dragon", "Dragon")]
    public void StripMarkup_returns_plain_label(string input, string expected)
        => FivetoolsJson.StripMarkup(input).Should().Be(expected);

    [Fact]
    public void StringList_reads_string_array()
    {
        using var doc = JsonDocument.Parse("[\"a\",\"b\"]");
        FivetoolsJson.StringList(doc.RootElement).Should().Equal("a", "b");
    }
}