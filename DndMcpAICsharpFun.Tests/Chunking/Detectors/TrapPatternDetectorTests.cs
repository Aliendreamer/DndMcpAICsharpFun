using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

namespace DndMcpAICsharpFun.Tests.Chunking.Detectors;

public sealed class TrapPatternDetectorTests
{
    private readonly TrapPatternDetector _sut = new();

    [Fact]
    public void Category_IsTrap() => Assert.Equal(ContentCategory.Trap, _sut.Category);

    [Theory]
    [InlineData("Trigger: Pressure plate\nEffect: 3d6 piercing\nDisarm DC: 15", 1.0f)]
    [InlineData("Trigger: Pressure plate\nEffect: 3d6 piercing", 0.6666667f)]
    [InlineData("Trigger: Pressure plate", 0.3333333f)]
    [InlineData("Some random text", 0.0f)]
    public void Detect_ReturnsExpectedScore(string text, float expected)
    {
        float score = _sut.Detect(text);
        Assert.Equal(expected, score, precision: 5);
    }

    [Theory]
    [InlineData("Trigger: Pressure plate activated", true)]
    [InlineData("TRIGGER: tripwire", true)]
    [InlineData("Effect: 3d6 piercing damage", false)]
    [InlineData("Some line", false)]
    public void IsEntityBoundary_ReturnsExpected(string line, bool expected)
    {
        Assert.Equal(expected, _sut.IsEntityBoundary(line));
    }
}
