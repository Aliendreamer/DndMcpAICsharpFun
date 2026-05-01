using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public class TocCategoryMapTests
{
    [Fact]
    public void GetCategory_ReturnsNull_WhenNoRanges()
    {
        var map = new TocCategoryMap([]);
        Assert.Null(map.GetCategory(5));
    }

    [Fact]
    public void GetCategory_ReturnsNull_WhenPageBeforeFirstBookmark()
    {
        var map = new TocCategoryMap([(10, ContentCategory.Spell)]);
        Assert.Null(map.GetCategory(5));
    }

    [Fact]
    public void GetCategory_ReturnsCategory_WhenPageWithinRange()
    {
        var map = new TocCategoryMap([
            (10, ContentCategory.Rule),
            (45, ContentCategory.Class),
            (200, ContentCategory.Spell)
        ]);
        Assert.Equal(ContentCategory.Class, map.GetCategory(100));
        Assert.Equal(ContentCategory.Spell, map.GetCategory(250));
        Assert.Equal(ContentCategory.Rule, map.GetCategory(10));
    }

    [Fact]
    public void GetCategory_ReturnsNull_ForNullMappedRange()
    {
        var map = new TocCategoryMap([
            (1, null),
            (45, ContentCategory.Class),
        ]);
        Assert.Null(map.GetCategory(3));
        Assert.Equal(ContentCategory.Class, map.GetCategory(45));
    }

    [Fact]
    public void IsEmpty_True_WhenNoRanges()
    {
        Assert.True(new TocCategoryMap([]).IsEmpty);
    }
}
