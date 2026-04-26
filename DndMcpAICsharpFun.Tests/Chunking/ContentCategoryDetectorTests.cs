using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Chunking;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

namespace DndMcpAICsharpFun.Tests.Chunking;

public sealed class ContentCategoryDetectorTests
{
    private static IPatternDetector MakeDetector(ContentCategory category, float score, bool isBoundary = false)
    {
        var d = Substitute.For<IPatternDetector>();
        d.Category.Returns(category);
        d.Detect(Arg.Any<string>()).Returns(score);
        d.IsEntityBoundary(Arg.Any<string>()).Returns(isBoundary);
        return d;
    }

    [Fact]
    public void Detect_HighConfidenceDetectorOverridesChapterDefault()
    {
        var spellDetector = MakeDetector(ContentCategory.Spell, 1.0f);
        var sut = new ContentCategoryDetector([spellDetector]);

        ContentCategory result = sut.Detect("Casting Time: 1 action", ContentCategory.Rule);

        Assert.Equal(ContentCategory.Spell, result);
    }

    [Fact]
    public void Detect_BelowThresholdFallsBackToChapterDefault()
    {
        var lowDetector = MakeDetector(ContentCategory.Spell, 0.5f);
        var sut = new ContentCategoryDetector([lowDetector]);

        ContentCategory result = sut.Detect("some text", ContentCategory.Rule);

        Assert.Equal(ContentCategory.Rule, result);
    }

    [Fact]
    public void Detect_ExactlyAtThresholdReturnsDetectorCategory()
    {
        var detector = MakeDetector(ContentCategory.Monster, 0.7f);
        var sut = new ContentCategoryDetector([detector]);

        ContentCategory result = sut.Detect("text", ContentCategory.Rule);

        Assert.Equal(ContentCategory.Monster, result);
    }

    [Fact]
    public void Detect_BestDetectorWins_WhenMultipleAboveThreshold()
    {
        var spellDetector = MakeDetector(ContentCategory.Spell, 0.75f);
        var monsterDetector = MakeDetector(ContentCategory.Monster, 1.0f);
        var sut = new ContentCategoryDetector([spellDetector, monsterDetector]);

        ContentCategory result = sut.Detect("text", ContentCategory.Rule);

        Assert.Equal(ContentCategory.Monster, result);
    }

    [Fact]
    public void FindBoundaryDetector_ReturnsMatchingDetector()
    {
        var nonBoundary = MakeDetector(ContentCategory.Rule, 0f, isBoundary: false);
        var boundary = MakeDetector(ContentCategory.Spell, 0f, isBoundary: true);
        var sut = new ContentCategoryDetector([nonBoundary, boundary]);

        IPatternDetector? result = sut.FindBoundaryDetector("any line");

        Assert.Same(boundary, result);
    }

    [Fact]
    public void FindBoundaryDetector_ReturnsNull_WhenNoMatch()
    {
        var d1 = MakeDetector(ContentCategory.Rule, 0f, isBoundary: false);
        var d2 = MakeDetector(ContentCategory.Spell, 0f, isBoundary: false);
        var sut = new ContentCategoryDetector([d1, d2]);

        Assert.Null(sut.FindBoundaryDetector("any line"));
    }
}
