using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

namespace DndMcpAICsharpFun.Tests.Chunking.Detectors;

public sealed class TreasurePatternDetectorTests
{
    private readonly TreasurePatternDetector _sut = new();

    [Fact]
    public void Category_IsTreasure() => Assert.Equal(ContentCategory.Treasure, _sut.Category);

    [Theory]
    [InlineData("Treasure Hoard: CR 1-4\nArt Objects: 25 gp\nGemstones: 10 gp", 1.0f)]
    [InlineData("Treasure Hoard: CR 1-4\nArt Objects: 25 gp", 0.6666667f)]
    [InlineData("Treasure Hoard: CR 1-4", 0.3333333f)]
    [InlineData("Some random text", 0.0f)]
    public void Detect_ReturnsExpectedScore(string text, float expected)
    {
        float score = _sut.Detect(text);
        Assert.Equal(expected, score, precision: 5);
    }

    [Theory]
    [InlineData("Treasure Hoard: CR 5-10", true)]
    [InlineData("TREASURE HOARD: CR 11-16", true)]
    [InlineData("Art Objects: 250 gp", false)]
    [InlineData("Some line", false)]
    public void IsEntityBoundary_ReturnsExpected(string line, bool expected)
    {
        Assert.Equal(expected, _sut.IsEntityBoundary(line));
    }
}
