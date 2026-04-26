using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

namespace DndMcpAICsharpFun.Tests.Chunking.Detectors;

public sealed class EncounterPatternDetectorTests
{
    private readonly EncounterPatternDetector _sut = new();

    [Fact]
    public void Category_IsEncounter() => Assert.Equal(ContentCategory.Encounter, _sut.Category);

    [Theory]
    [InlineData("Encounter Difficulty: Hard\nXP Threshold: 500\nRandom Encounter: Roll d6", 1.0f)]
    [InlineData("Encounter Difficulty: Easy\nXP Threshold: 100", 0.6666667f)]
    [InlineData("Encounter Difficulty: Medium", 0.3333333f)]
    [InlineData("Some random text", 0.0f)]
    public void Detect_ReturnsExpectedScore(string text, float expected)
    {
        float score = _sut.Detect(text);
        Assert.Equal(expected, score, precision: 5);
    }

    [Theory]
    [InlineData("Encounter Difficulty: Hard", true)]
    [InlineData("Random Encounter: Roll 1d6", true)]
    [InlineData("ENCOUNTER DIFFICULTY: Easy", true)]
    [InlineData("XP Threshold: 500", false)]
    [InlineData("Some line", false)]
    public void IsEntityBoundary_ReturnsExpected(string line, bool expected)
    {
        Assert.Equal(expected, _sut.IsEntityBoundary(line));
    }
}
