using DndMcpAICsharpFun.Features.Dice;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Dice;

public sealed class DiceRollerTests
{
    // Returns scripted values in order; Next(min,max) returns next script value (asserted in range by the test cases).
    private sealed class ScriptedRng(params int[] vals) : IRandomSource
    {
        private int _i;
        public int Next(int min, int max) => vals[_i++];
    }

    [Fact]
    public void Rolls_within_range_with_real_rng()
    {
        var r = new DiceRoller(new SystemRandomSource());
        var res = r.Roll(DiceExpression.Parse("3d6"));
        res.Dice.Should().HaveCount(3);
        res.Dice.Should().OnlyContain(v => v >= 1 && v <= 6);
    }

    [Fact]
    public void Modifier_applied_and_dice_reported()
    {
        var r = new DiceRoller(new ScriptedRng(4, 5));
        var res = r.Roll(DiceExpression.Parse("2d6+3"));
        res.Dice.Should().Equal(4, 5);
        res.Total.Should().Be(12);
        res.Breakdown.Should().Be("2d6+3 → [4,5]+3 = 12");
    }

    [Fact]
    public void Advantage_keeps_higher()
    {
        var r = new DiceRoller(new ScriptedRng(18, 7));
        var res = r.Roll(DiceExpression.Parse("d20 adv"));
        res.Dice.Should().Equal(18, 7);
        res.Kept.Should().Equal(18);
        res.Total.Should().Be(18);
        res.Breakdown.Should().Be("d20 (adv) → [18,7] → 18");
    }

    [Fact]
    public void Disadvantage_keeps_lower()
    {
        var r = new DiceRoller(new ScriptedRng(18, 7));
        var res = r.Roll(DiceExpression.Parse("d20 dis"));
        res.Kept.Should().Equal(7);
        res.Total.Should().Be(7);
        res.Breakdown.Should().Be("d20 (dis) → [18,7] → 7");
    }

    [Fact]
    public void Same_script_is_reproducible()
    {
        var a = new DiceRoller(new ScriptedRng(3, 3, 3)).Roll(DiceExpression.Parse("3d6"));
        var b = new DiceRoller(new ScriptedRng(3, 3, 3)).Roll(DiceExpression.Parse("3d6"));
        a.Should().BeEquivalentTo(b);
    }
}