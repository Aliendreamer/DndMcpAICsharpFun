using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class BookmarkTocMapperTests
{
    [Fact]
    public void Map_EmptyList_ReturnsEmpty()
    {
        var result = BookmarkTocMapper.Map([]);
        Assert.Empty(result);
    }

    [Fact]
    public void Map_PreservesOrder()
    {
        var input = new[]
        {
            new PdfBookmark("Spells", 100),
            new PdfBookmark("Monsters", 200),
            new PdfBookmark("Equipment", 300),
        };

        var result = BookmarkTocMapper.Map(input);

        Assert.Equal(3, result.Count);
        Assert.Equal("Spells", result[0].Title);
        Assert.Equal("Monsters", result[1].Title);
        Assert.Equal("Equipment", result[2].Title);
    }

    [Theory]
    [InlineData("Spell Descriptions", ContentCategory.Spell)]
    [InlineData("SPELLS", ContentCategory.Spell)]
    [InlineData("Monsters", ContentCategory.Monster)]
    [InlineData("Bestiary", ContentCategory.Monster)]
    [InlineData("Creature Statistics", ContentCategory.Monster)]
    [InlineData("Adventuring Gear", ContentCategory.Item)]
    [InlineData("Weapons", ContentCategory.Item)]
    [InlineData("Magic Items", ContentCategory.Item)]
    [InlineData("Backgrounds", ContentCategory.Background)]
    [InlineData("Races", ContentCategory.Race)]
    [InlineData("Species", ContentCategory.Race)]
    [InlineData("Classes", ContentCategory.Class)]
    [InlineData("Wizard", ContentCategory.Class)]
    [InlineData("Conditions", ContentCategory.Condition)]
    [InlineData("Gods of the Multiverse", ContentCategory.God)]
    [InlineData("Pantheon", ContentCategory.God)]
    [InlineData("Planes of Existence", ContentCategory.Plane)]
    [InlineData("Cosmology", ContentCategory.Plane)]
    [InlineData("Treasure", ContentCategory.Treasure)]
    [InlineData("Encounters", ContentCategory.Encounter)]
    [InlineData("Traps", ContentCategory.Trap)]
    [InlineData("Feats", ContentCategory.Trait)]
    [InlineData("Personality and Background", ContentCategory.Trait)]
    [InlineData("World History", ContentCategory.Lore)]
    [InlineData("Combat", ContentCategory.Combat)]
    [InlineData("Adventuring", ContentCategory.Adventuring)]
    [InlineData("Resting", ContentCategory.Adventuring)]
    public void Map_KeywordTitles_AssignExpectedCategory(string title, ContentCategory expected)
    {
        var result = BookmarkTocMapper.Map([new PdfBookmark(title, 1)]);
        Assert.Equal(expected, result[0].Category);
    }

    [Theory]
    [InlineData("Preface")]
    [InlineData("Acknowledgements")]
    [InlineData("Credits")]
    public void Map_UnrecognisedTitle_FallsBackToRule(string title)
    {
        var result = BookmarkTocMapper.Map([new PdfBookmark(title, 1)]);
        Assert.Equal(ContentCategory.Rule, result[0].Category);
    }

    [Fact]
    public void Map_StartPageMatchesBookmarkPageNumber()
    {
        var input = new[] { new PdfBookmark("Spells", 99) };
        var result = BookmarkTocMapper.Map(input);
        Assert.Equal(99, result[0].StartPage);
    }

    [Fact]
    public void Map_LeafWithoutKeyword_InheritsParentCategory()
    {
        var input = new[]
        {
            new PdfBookmark("Aboleth",  10, ParentTitle: "Monsters (A-Z)"),
            new PdfBookmark("Beholder", 28, ParentTitle: "Monsters (A-Z)"),
            new PdfBookmark("Goblin",   166, ParentTitle: "Monsters (A-Z)"),
        };
        var result = BookmarkTocMapper.Map(input);
        Assert.All(result, e => Assert.Equal(ContentCategory.Monster, e.Category));
    }

    [Fact]
    public void Map_LeafWithOwnKeyword_DoesNotInheritParent()
    {
        // "Magic Items" matches Item directly even though parent is Treasure
        var input = new[] { new PdfBookmark("Magic Items", 200, ParentTitle: "Treasure") };
        var result = BookmarkTocMapper.Map(input);
        Assert.Equal(ContentCategory.Item, result[0].Category);
    }

    [Fact]
    public void Map_NoParent_FallsBackToRule()
    {
        var input = new[] { new PdfBookmark("Aboleth", 10) };
        var result = BookmarkTocMapper.Map(input);
        Assert.Equal(ContentCategory.Rule, result[0].Category);
    }

    [Theory]
    [InlineData("Aberrations")]
    [InlineData("Beasts")]
    [InlineData("Celestials")]
    [InlineData("Dragons")]
    [InlineData("Elementals")]
    [InlineData("Fey")]
    [InlineData("Fiends")]
    [InlineData("Giants")]
    [InlineData("Humanoids")]
    [InlineData("Monstrosities")]
    [InlineData("Oozes")]
    [InlineData("Plants")]
    [InlineData("Undead")]
    [InlineData("Nonplayer Characters")]
    public void Map_CreatureTypeKeyword_AssignsMonster(string title)
    {
        var result = BookmarkTocMapper.Map([new PdfBookmark(title, 1)]);
        Assert.Equal(ContentCategory.Monster, result[0].Category);
    }
}
