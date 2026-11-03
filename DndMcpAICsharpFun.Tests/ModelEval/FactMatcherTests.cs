using FluentAssertions;

using DndMcpAICsharpFun.Tools.ModelEval.Full;

namespace DndMcpAICsharpFun.Tests.ModelEval;

public class FactMatcherTests
{
    private static Fact F(params string[] variants) => new(variants);

    [Fact]
    public void Normalize_StripsCaseWhitespaceCommas()
    {
        FactMatcher.Normalize("2,000 GP").Should().Be("2000gp");
    }

    [Fact]
    public void AllMatch_RequiresEveryFact()
    {
        var facts = new[] { F("d6"), F("intelligence") };

        FactMatcher.AllMatch(facts, "Wizards use a d6").Should().BeFalse();
        FactMatcher.AllMatch(facts, "...d6... Intelligence and Wisdom").Should().BeTrue();
    }

    [Fact]
    public void AllMatch_AnyVariantSuffices()
    {
        var facts = new[] { F("3rd-level", "level 3", "3rd level") };

        FactMatcher.AllMatch(facts, "It's a level 3 spell").Should().BeTrue();
    }

    [Fact]
    public void AllMatch_DoesNotMatchInsideLongerNumber()
    {
        FactMatcher.AllMatch([F("d6")], "a d60 die").Should().BeFalse();
        FactMatcher.AllMatch([F("level 1")], "a level 10 spell").Should().BeFalse();
        FactMatcher.AllMatch([F("d10")], "roll d100").Should().BeFalse();
        FactMatcher.AllMatch([F("50 feet")], "150 feet").Should().BeFalse();
    }

    [Fact]
    public void AllMatch_LaterCleanOccurrenceMatches()
    {
        FactMatcher.AllMatch([F("d6")], "d60 or d6").Should().BeTrue();
    }

    [Fact]
    public void AllMatch_CommaNumbers()
    {
        FactMatcher.AllMatch([F("2000")], "2,000 gp").Should().BeTrue();
    }

    [Fact]
    public void AllMatch_IgnoresMarkdownEmphasis() =>
        FactMatcher.AllMatch([F("750 gp")], "That costs **750** gp in total").Should().BeTrue();

    [Fact]
    public void AllMatch_IgnoresLabelColonAndBackticks()
    {
        FactMatcher.AllMatch([F("level 3")], "Level: 3").Should().BeTrue();
        FactMatcher.AllMatch([F("d6")], "hit die: `d6`").Should().BeTrue();
    }

    [Theory]
    [InlineData("3rd\u2011level spell")]
    [InlineData("3rd\u2013level spell")]
    [InlineData("3rd\u2212level spell")]
    public void AllMatch_FoldsUnicodeHyphens(string text) =>
        FactMatcher.AllMatch([F("3rd-level")], text).Should().BeTrue();

    [Fact]
    public void AllMatch_DigitBoundaryStillHolds_AfterNoiseStripping() =>
        FactMatcher.AllMatch([F("d6")], "a d**60** die").Should().BeFalse();
}
