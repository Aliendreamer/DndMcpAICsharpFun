using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

namespace DndMcpAICsharpFun.Tests.Chunking.Detectors;

public sealed class SpellPatternDetectorTests
{
    private readonly SpellPatternDetector _sut = new();

    [Fact]
    public void Category_IsSpell() => Assert.Equal(ContentCategory.Spell, _sut.Category);

    [Theory]
    [InlineData("Casting Time: 1 action\nRange: 60 feet\nComponents: V, S\nDuration: Instantaneous", 1.0f)]
    [InlineData("Casting Time: 1 action\nRange: 60 feet\nComponents: V, S", 0.75f)]
    [InlineData("Casting Time: 1 action", 0.25f)]
    [InlineData("Some random text with no spell keywords", 0.0f)]
    public void Detect_ReturnsExpectedScore(string text, float expected)
    {
        float score = _sut.Detect(text);
        Assert.Equal(expected, score, precision: 5);
    }

    [Theory]
    [InlineData("Casting Time: 1 action", true)]
    [InlineData("CASTING TIME: bonus action", true)]
    [InlineData("Range: 60 feet", false)]
    [InlineData("Some other line", false)]
    public void IsEntityBoundary_ReturnsExpected(string line, bool expected)
    {
        Assert.Equal(expected, _sut.IsEntityBoundary(line));
    }
}
