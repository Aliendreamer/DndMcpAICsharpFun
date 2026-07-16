using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class FullCoverageHeadingTocMapperTests
{
    private static PdfStructureItem H(string text, int page) => new("section_header", text, page, 1);

    [Fact]
    public void Map_EveryHeadingBecomesATitledEntry()
    {
        var entries = FullCoverageHeadingTocMapper.Map(
            new[] { H("Monsters", 1), H("Yuan-ti Anathema", 12) }, "Book");

        Assert.Equal("Monsters", entries[0].Title);
        Assert.Equal("Yuan-ti Anathema", entries[1].Title);
    }

    [Fact]
    public void Map_SubHeadingInheritsEnclosingConfidentCategory()
    {
        var entries = FullCoverageHeadingTocMapper.Map(
            new[] { H("Monsters", 1), H("Yuan-ti Anathema", 12) }, "Book");

        Assert.Equal(ContentCategory.Monster, entries[0].Category);
        Assert.Equal(ContentCategory.Monster, entries[1].Category); // carried forward
    }

    [Fact]
    public void Map_HeadingBeforeAnyConfidentCategoryDefaultsToRule()
    {
        var entries = FullCoverageHeadingTocMapper.Map(
            new[] { H("Yuan-ti Anathema", 12) }, "Book");

        Assert.Equal(ContentCategory.Rule, entries[^1].Category);
    }

    [Fact]
    public void Map_FirstHeadingAfterPageOne_PrependsFrontMatterCatchAll()
    {
        var entries = FullCoverageHeadingTocMapper.Map(new[] { H("Monsters", 10) }, "Book");

        Assert.Equal("Front Matter", entries[0].Title);
        Assert.Equal(1, entries[0].StartPage);
        Assert.Equal(ContentCategory.Rule, entries[0].Category);
    }

    [Fact]
    public void Map_NoHeadings_YieldsSingleWholeBookCatchAll()
    {
        var entries = FullCoverageHeadingTocMapper.Map(Array.Empty<PdfStructureItem>(), "My Book");

        Assert.Single(entries);
        Assert.Equal("My Book", entries[0].Title);
        Assert.Equal(1, entries[0].StartPage);
    }

    [Fact]
    public void Map_ProducesGapFreeCoverage_EveryPageResolves()
    {
        var entries = FullCoverageHeadingTocMapper.Map(
            new[] { H("Intro", 3), H("Monsters", 10) }, "Book");
        var map = new TocCategoryMap(entries);

        for (var page = 1; page <= 50; page++)
            Assert.NotNull(map.GetEntry(page)); // no page falls into a gap
    }
}
