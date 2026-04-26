using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

namespace DndMcpAICsharpFun.Tests.Chunking.Detectors;

public sealed class BackgroundPatternDetectorTests
{
    private readonly BackgroundPatternDetector _sut = new();

    [Fact]
    public void Category_IsBackground() => Assert.Equal(ContentCategory.Background, _sut.Category);

    [Theory]
    [InlineData("Skill Proficiencies: Stealth\nFeature: Criminal Contact", 1.0f)]
    [InlineData("Skill Proficiencies: Stealth", 0.5f)]
    [InlineData("Feature: Criminal Contact", 0.5f)]
    [InlineData("No background keywords", 0.0f)]
    public void Detect_ReturnsExpectedScore(string text, float expected)
    {
        float score = _sut.Detect(text);
        Assert.Equal(expected, score, precision: 5);
    }

    [Theory]
    [InlineData("Skill Proficiencies: Stealth, Deception", true)]
    [InlineData("SKILL PROFICIENCIES: Athletics", true)]
    [InlineData("Feature: Criminal Contact", false)]
    [InlineData("Some line", false)]
    public void IsEntityBoundary_ReturnsExpected(string line, bool expected)
    {
        Assert.Equal(expected, _sut.IsEntityBoundary(line));
    }
}
