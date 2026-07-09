using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class HeadingTocMapperTests
{
    private static PdfStructureItem H(string text, int page) => new("section_header", text, page, 1);

    [Fact]
    public void Map_EmitsOnlyConfidentHeadings_DroppingKeywordlessSubHeadings()
    {
        var headings = new[] { H("Barbarian", 1), H("Rage", 2), H("Hill Dwarf", 3), H("Spells", 4) };

        var entries = HeadingTocMapper.Map(headings);

        entries.Should().HaveCount(2);
        entries[0].Title.Should().Be("Barbarian");
        entries[0].Category.Should().Be(ContentCategory.Class);
        entries[0].StartPage.Should().Be(1);
        entries[1].Title.Should().Be("Spells");
        entries[1].Category.Should().Be(ContentCategory.Spell);
        entries[1].StartPage.Should().Be(4);
    }

    [Fact]
    public void Map_SkipsBlankTitles()
    {
        var entries = HeadingTocMapper.Map(new[] { H("   ", 1), H("Races", 2) });
        entries.Should().ContainSingle();
        entries[0].Category.Should().Be(ContentCategory.Race);
    }

    [Fact]
    public void Map_DropsNonEntityCategories()
    {
        var entries = HeadingTocMapper.Map(new[] { H("Combat", 1), H("Adventuring", 2) });
        entries.Should().BeEmpty();
    }

    [Fact]
    public void Map_FeedsTocCategoryMap_SoRangesPropagate()
    {
        var headings = new[] { H("Barbarian", 1), H("Rage", 2), H("Hill Dwarf", 3), H("Spells", 4) };

        var map = new TocCategoryMap(HeadingTocMapper.Map(headings));

        map.GetCategory(3).Should().Be(ContentCategory.Class);
        map.GetCategory(4).Should().Be(ContentCategory.Spell);
    }
}