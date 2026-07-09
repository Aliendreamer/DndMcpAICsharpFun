using DndMcpAICsharpFun.Features.Dice;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Dice;

public sealed class DiceExpressionTests
{
    [Fact]
    public void Parses_count_die_modifier()
    {
        DiceExpression.TryParse("2d6+3", out var e, out var err).Should().BeTrue();
        err.Should().BeNull();
        e.Should().Be(new DiceExpression(2, 6, 3, RollMode.Normal));
    }

    [Fact]
    public void Bare_die_defaults_count_1_mod_0()
    {
        DiceExpression.TryParse("d20", out var e, out _).Should().BeTrue();
        e.Should().Be(new DiceExpression(1, 20, 0, RollMode.Normal));
    }

    [Theory]
    [InlineData("1d20-1", 1, 20, -1)]
    [InlineData("4d8", 4, 8, 0)]
    [InlineData("1d100+10", 1, 100, 10)]
    public void Parses_variants(string s, int c, int d, int m)
    {
        DiceExpression.TryParse(s, out var e, out _).Should().BeTrue();
        e.Should().Be(new DiceExpression(c, d, m, RollMode.Normal));
    }

    [Fact]
    public void Advantage_on_single_d20_ok()
    {
        DiceExpression.TryParse("d20 adv", out var e, out _).Should().BeTrue();
        e.Mode.Should().Be(RollMode.Advantage);
    }

    [Theory]
    [InlineData("1d7")]           // unsupported die
    [InlineData("2d20 adv")]      // adv on multiple
    [InlineData("1d6 adv")]       // adv on non-d20
    [InlineData("999d100")]       // over MaxCount
    [InlineData("0d6")]           // count < 1
    [InlineData("hello")]         // garbage
    [InlineData("")]              // empty
    public void Invalid_inputs_are_rejected_without_throwing(string s)
    {
        DiceExpression.TryParse(s, out _, out var err).Should().BeFalse();
        err.Should().NotBeNullOrEmpty();
    }


    [Fact]
    public void Oversized_count_is_rejected_without_throwing()
    {
        var result = false;
        string? error = null;
        Action act = () => result = DiceExpression.TryParse("99999999999d6", out _, out error);

        act.Should().NotThrow();
        result.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Oversized_modifier_is_rejected_without_throwing()
    {
        var result = false;
        string? error = null;
        Action act = () => result = DiceExpression.TryParse("1d6+99999999999", out _, out error);

        act.Should().NotThrow();
        result.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }


    [Theory]
    [InlineData("1d6+1001")]
    [InlineData("1d6-1001")]
    public void Modifier_magnitude_over_cap_is_rejected(string s)
    {
        DiceExpression.TryParse(s, out _, out var err).Should().BeFalse();
        err.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("1d6+1000", 1000)]
    [InlineData("1d6-1000", -1000)]
    public void Modifier_magnitude_at_cap_is_accepted(string s, int modifier)
    {
        DiceExpression.TryParse(s, out var e, out var err).Should().BeTrue();
        err.Should().BeNull();
        e.Modifier.Should().Be(modifier);
    }
}