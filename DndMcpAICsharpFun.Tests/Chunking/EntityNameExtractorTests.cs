using DndMcpAICsharpFun.Features.Ingestion.Chunking;

namespace DndMcpAICsharpFun.Tests.Chunking;

public sealed class EntityNameExtractorTests
{
    [Fact]
    public void Extract_ReturnsTrimmedLineBefore_BoundaryIndex()
    {
        var lines = new List<string> { "  Fireball  ", "Casting Time: 1 action" };
        string? result = EntityNameExtractor.Extract(lines, boundaryIndex: 1);
        Assert.Equal("Fireball", result);
    }

    [Fact]
    public void Extract_ReturnsNull_WhenBoundaryAtIndex0()
    {
        var lines = new List<string> { "Casting Time: 1 action" };
        string? result = EntityNameExtractor.Extract(lines, boundaryIndex: 0);
        Assert.Null(result);
    }

    [Fact]
    public void Extract_SkipsEmptyLines_WithinLookback()
    {
        var lines = new List<string> { "Fireball", "", "Casting Time: 1 action" };
        string? result = EntityNameExtractor.Extract(lines, boundaryIndex: 2);
        Assert.Equal("Fireball", result);
    }

    [Fact]
    public void Extract_ReturnsNull_WhenAllPrecedingLinesWithinLookbackAreEmpty()
    {
        var lines = new List<string> { "Far away", "", "", "Casting Time: 1 action" };
        // lookback is 3, so it checks indices 2, 1, 0 — indices 2 and 1 are empty,
        // index 0 is beyond the 3-line lookback window from boundary 3
        string? result = EntityNameExtractor.Extract(lines, boundaryIndex: 3);
        Assert.Equal("Far away", result);
    }
}
