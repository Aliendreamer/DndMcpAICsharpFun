using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Chunking;

namespace DndMcpAICsharpFun.Tests.Chunking;

public sealed class ChapterContextTrackerTests
{
    [Fact]
    public void ProcessLine_NonChapterLine_DoesNotChangeState()
    {
        var sut = new ChapterContextTracker();
        sut.ProcessLine("Casting Time: 1 action");
        Assert.Equal(string.Empty, sut.CurrentChapter);
        Assert.Equal(ContentCategory.Rule, sut.CurrentCategory);
    }

    [Theory]
    [InlineData("Chapter 11: Spells", ContentCategory.Spell)]
    [InlineData("Chapter 3: Magic and Cantrips", ContentCategory.Spell)]
    [InlineData("Chapter 5: Monsters and Bestiary", ContentCategory.Monster)]
    [InlineData("Chapter 2: Creature Types", ContentCategory.Monster)]
    [InlineData("Chapter 3: Character Classes", ContentCategory.Class)]
    [InlineData("Chapter 4: Backgrounds", ContentCategory.Background)]
    [InlineData("Chapter 5: Equipment and Weapons", ContentCategory.Item)]
    [InlineData("Chapter 8: Armor and Gear", ContentCategory.Item)]
    [InlineData("Appendix A: Conditions", ContentCategory.Rule)]
    public void ProcessLine_ChapterLine_SetsCorrectCategory(string line, ContentCategory expectedCategory)
    {
        var sut = new ChapterContextTracker();
        sut.ProcessLine(line);
        Assert.Equal(expectedCategory, sut.CurrentCategory);
        Assert.Equal(line.Trim(), sut.CurrentChapter);
    }

    [Fact]
    public void ProcessLine_ChapterLineNoKnownPattern_DoesNotChangeState()
    {
        var sut = new ChapterContextTracker();
        sut.ProcessLine("Chapter 1: Introduction");
        Assert.Equal(string.Empty, sut.CurrentChapter);
        Assert.Equal(ContentCategory.Rule, sut.CurrentCategory);
    }

    [Fact]
    public void ProcessLine_UpdatesChapterOnSubsequentCalls()
    {
        var sut = new ChapterContextTracker();
        sut.ProcessLine("Chapter 5: Monsters and Bestiary");
        sut.ProcessLine("Chapter 11: Spells");
        Assert.Equal(ContentCategory.Spell, sut.CurrentCategory);
        Assert.Equal("Chapter 11: Spells", sut.CurrentChapter);
    }
}
