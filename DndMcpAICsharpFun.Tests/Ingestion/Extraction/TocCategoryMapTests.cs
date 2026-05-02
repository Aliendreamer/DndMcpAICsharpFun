using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public class TocCategoryMapTests
{
    [Fact]
    public void GetCategory_ReturnsNull_WhenNoEntries()
    {
        var map = new TocCategoryMap([]);
        Assert.Null(map.GetCategory(5));
    }

    [Fact]
    public void GetCategory_ReturnsNull_WhenPageBeforeFirstEntry()
    {
        var map = new TocCategoryMap([new TocSectionEntry("Spells", ContentCategory.Spell, 10, 50)]);
        Assert.Null(map.GetCategory(5));
    }

    [Fact]
    public void GetCategory_ReturnsCategory_WhenPageWithinRange()
    {
        var entries = new[]
        {
            new TocSectionEntry("Rules", ContentCategory.Rule, 10, 44),
            new TocSectionEntry("Classes", ContentCategory.Class, 45, 199),
            new TocSectionEntry("Spells", ContentCategory.Spell, 200, 280)
        };
        var map = new TocCategoryMap(entries);
        Assert.Equal(ContentCategory.Class, map.GetCategory(100));
        Assert.Equal(ContentCategory.Spell, map.GetCategory(250));
        Assert.Equal(ContentCategory.Rule, map.GetCategory(10));
    }

    [Fact]
    public void GetCategory_ReturnsNull_WhenCategoryIsNull()
    {
        var entries = new[]
        {
            new TocSectionEntry("Intro", null, 1, 44),
            new TocSectionEntry("Classes", ContentCategory.Class, 45, 199),
        };
        var map = new TocCategoryMap(entries);
        Assert.Null(map.GetCategory(3));
        Assert.Equal(ContentCategory.Class, map.GetCategory(45));
    }

    [Fact]
    public void IsEmpty_True_WhenNoEntries()
    {
        Assert.True(new TocCategoryMap([]).IsEmpty);
    }

    // --- GetEntry tests ---

    [Fact]
    public void GetEntry_ReturnsNull_WhenMapIsEmpty()
    {
        var map = new TocCategoryMap([]);
        Assert.Null(map.GetEntry(5));
    }

    [Fact]
    public void GetEntry_ReturnsEntry_WhenPageWithinRange()
    {
        var entries = new[]
        {
            new TocSectionEntry("Spells", ContentCategory.Spell, 200, 280),
            new TocSectionEntry("Monsters", ContentCategory.Monster, 300, null)
        };
        var map = new TocCategoryMap(entries);

        var entry = map.GetEntry(250);
        Assert.NotNull(entry);
        Assert.Equal("Spells", entry!.Title);
        Assert.Equal(ContentCategory.Spell, entry.Category);
    }

    [Fact]
    public void GetEntry_ReturnsNull_WhenPageBeforeAllSections()
    {
        var map = new TocCategoryMap([new TocSectionEntry("Classes", ContentCategory.Class, 45, 112)]);
        Assert.Null(map.GetEntry(10));
    }

    [Fact]
    public void GetEntry_ReturnsNull_WhenPageAfterSectionEnd()
    {
        var map = new TocCategoryMap([new TocSectionEntry("Classes", ContentCategory.Class, 45, 112)]);
        Assert.Null(map.GetEntry(113));
    }

    [Fact]
    public void GetEntry_ComputesMissingEndPage_FromNextEntryStart()
    {
        var entries = new[]
        {
            new TocSectionEntry("Spells", ContentCategory.Spell, 200, null),
            new TocSectionEntry("Monsters", ContentCategory.Monster, 300, null)
        };
        var map = new TocCategoryMap(entries);

        var spellEntry = map.GetEntry(250);
        Assert.NotNull(spellEntry);
        Assert.Equal(299, spellEntry!.EndPage);

        Assert.Null(map.GetEntry(199));
        Assert.Equal("Monsters", map.GetEntry(300)!.Title);
    }

    [Fact]
    public void GetEntry_LastEntryWithNoEndPage_CoversAllHigherPages()
    {
        var map = new TocCategoryMap([new TocSectionEntry("Appendix", ContentCategory.Rule, 500, null)]);
        Assert.NotNull(map.GetEntry(99999));
    }

    [Fact]
    public void GetCategory_DelegatesToGetEntry()
    {
        var entries = new[] { new TocSectionEntry("Classes", ContentCategory.Class, 45, 112) };
        var map = new TocCategoryMap(entries);
        Assert.Equal(ContentCategory.Class, map.GetCategory(80));
        Assert.Null(map.GetCategory(10));
    }
}
