using DndMcpAICsharpFun.Tools.ModelEval.Full;

using FluentAssertions;

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
    public void AllMatch_DecodesJsonUnicodeEscapes()
    {
        FactMatcher.AllMatch([F("+2")], "{\"text\":\"a \\u002B2 bonus\"}").Should().BeTrue();
        FactMatcher.AllMatch([F("couldn't find")], "I couldn\\u0027t find it").Should().BeTrue();
    }

    [Fact]
    public void AllMatch_FoldsCurlyApostrophes()
    {
        FactMatcher.AllMatch([F("couldn't find")], "I couldn’t find it").Should().BeTrue();
        FactMatcher.AllMatch([F("couldn't find")], "I couldn‘t find it").Should().BeTrue();
    }

    [Fact]
    public void AllMatch_FractionIsNotNumericPrefix()
    {
        FactMatcher.AllMatch([F("challenge rating 1")], "challenge rating 1/2").Should().BeFalse();
        FactMatcher.AllMatch([F("challenge rating 1/2")], "challenge rating 1/2").Should().BeTrue();
        FactMatcher.AllMatch([F("2")], "1/2").Should().BeFalse();
    }

    [Fact]
    public void AllMatch_HitPointsStatBlockForms()
    {
        var f = new[] { F("hit points 27", "hp 27") };
        FactMatcher.AllMatch(f, "Hit Points: 27 (5d8 + 5)").Should().BeTrue();
        FactMatcher.AllMatch(f, "HP 27").Should().BeTrue();
        FactMatcher.AllMatch(f, "HP 270").Should().BeFalse();
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

    [Fact]
    public void AllMatch_ParenthesizedCrForm_Matches() =>
        FactMatcher.AllMatch([F("(cr) of 3")], "a **Challenge Rating (CR) of 3**.").Should().BeTrue();

    [Theory]
    [InlineData("Stats: AC 17, 58 HP, STR 16", true)]
    [InlineData("HP 58 and nothing else", true)]
    [InlineData("She walks with pride", false)]
    public void AllMatch_RegexVariant_MatchesNormalizedText(string text, bool expected) =>
        FactMatcher.AllMatch([F(@"re:ac\d+", @"re:\d+hp", @"re:hp\d+")], text).Should().Be(expected);
}