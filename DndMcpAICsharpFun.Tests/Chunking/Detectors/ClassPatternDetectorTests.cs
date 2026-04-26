using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

namespace DndMcpAICsharpFun.Tests.Chunking.Detectors;

public sealed class ClassPatternDetectorTests
{
    private readonly ClassPatternDetector _sut = new();

    [Fact]
    public void Category_IsClass() => Assert.Equal(ContentCategory.Class, _sut.Category);

    [Theory]
    [InlineData("Hit Dice: 1d10\nProficiencies: Armor\nSaving Throws: Str", 1.0f)]
    [InlineData("Hit Dice: 1d10\nProficiencies: Armor", 0.6666667f)]
    [InlineData("Hit Dice: 1d10", 0.3333333f)]
    [InlineData("No class keywords here", 0.0f)]
    public void Detect_ReturnsExpectedScore(string text, float expected)
    {
        float score = _sut.Detect(text);
        Assert.Equal(expected, score, precision: 5);
    }

    [Theory]
    [InlineData("Hit Dice: 1d10 per level", true)]
    [InlineData("HIT DICE: 1d8", true)]
    [InlineData("Proficiencies: Armor", false)]
    [InlineData("Some line", false)]
    public void IsEntityBoundary_ReturnsExpected(string line, bool expected)
    {
        Assert.Equal(expected, _sut.IsEntityBoundary(line));
    }
}
