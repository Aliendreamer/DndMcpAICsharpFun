using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public class PageBlockGrouperTests
{
    private static PageBlock H1(string text) => new(1, "h1", text);
    private static PageBlock H2(string text) => new(1, "h2", text);
    private static PageBlock H3(string text) => new(1, "h3", text);
    private static PageBlock Body(string text) => new(1, "body", text);

    [Fact]
    public void Group_EmptyBlocks_ReturnsEmptyList()
    {
        var result = PageBlockGrouper.Group([]);
        Assert.Empty(result);
    }

    [Fact]
    public void Group_OnlyBodyBlocks_ReturnsSingleGroup()
    {
        var blocks = new[] { Body("para 1"), Body("para 2") };
        var result = PageBlockGrouper.Group(blocks);
        Assert.Single(result);
        Assert.Equal(2, result[0].Count);
    }

    [Fact]
    public void Group_H2StartsNewGroup()
    {
        var blocks = new[] { H2("Spells"), Body("spell info"), H2("Monsters"), Body("monster info") };
        var result = PageBlockGrouper.Group(blocks);
        Assert.Equal(2, result.Count);
        Assert.Equal("Spells", result[0][0].Text);
        Assert.Equal("Monsters", result[1][0].Text);
    }

    [Fact]
    public void Group_H1StartsNewGroup()
    {
        var blocks = new[] { H1("Chapter 1"), Body("intro"), H1("Chapter 2"), Body("content") };
        var result = PageBlockGrouper.Group(blocks);
        Assert.Equal(2, result.Count);
        Assert.Equal("Chapter 1", result[0][0].Text);
    }

    [Fact]
    public void Group_H3AppendedToCurrentGroup()
    {
        var blocks = new[] { H2("Warlock"), H3("Eldritch Invocations"), Body("invoc text") };
        var result = PageBlockGrouper.Group(blocks);
        Assert.Single(result);
        Assert.Equal(3, result[0].Count);
    }

    [Fact]
    public void Group_BodyBeforeAnyHeading_GoesIntoLeadingGroup()
    {
        var blocks = new[] { Body("preamble"), H2("Section A"), Body("section text") };
        var result = PageBlockGrouper.Group(blocks);
        Assert.Equal(2, result.Count);
        Assert.Single(result[0]); // preamble
        Assert.Equal(2, result[1].Count); // H2 + body
    }

    [Fact]
    public void Group_MultipleH2s_EachStartsNewGroup()
    {
        var blocks = new[]
        {
            H2("Wizard"), Body("wizard text"),
            H2("Warlock"), Body("warlock text"),
            H2("Sorcerer"), Body("sorcerer text")
        };
        var result = PageBlockGrouper.Group(blocks);
        Assert.Equal(3, result.Count);
        Assert.All(result, g => Assert.Equal(2, g.Count));
    }
}
