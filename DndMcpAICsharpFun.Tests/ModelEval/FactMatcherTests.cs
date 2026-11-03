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
}
