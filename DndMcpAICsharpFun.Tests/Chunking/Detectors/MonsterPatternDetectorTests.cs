using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

namespace DndMcpAICsharpFun.Tests.Chunking.Detectors;

public sealed class MonsterPatternDetectorTests
{
    private readonly MonsterPatternDetector _sut = new();

    [Fact]
    public void Category_IsMonster() => Assert.Equal(ContentCategory.Monster, _sut.Category);

    [Theory]
    [InlineData("Armor Class: 15\nHit Points: 45\nSpeed: 30 ft.", 1.0f)]
    [InlineData("Armor Class: 15\nHit Points: 45", 0.6666667f)]
    [InlineData("Armor Class: 15", 0.3333333f)]
    [InlineData("Some random text", 0.0f)]
    public void Detect_ReturnsExpectedScore(string text, float expected)
    {
        float score = _sut.Detect(text);
        Assert.Equal(expected, score, precision: 5);
    }

    [Theory]
    [InlineData("Armor Class: 15 (natural armor)", true)]
    [InlineData("ARMOR CLASS: 13", true)]
    [InlineData("Hit Points: 45", false)]
    [InlineData("Some line", false)]
    public void IsEntityBoundary_ReturnsExpected(string line, bool expected)
    {
        Assert.Equal(expected, _sut.IsEntityBoundary(line));
    }
}
