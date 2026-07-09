using DndMcpAICsharpFun.Features.Resolution;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class BreathWeaponRulesTests
{
    [Theory]
    [InlineData(1, "1d10")]
    [InlineData(5, "1d10")]
    [InlineData(6, "2d6")]
    [InlineData(10, "2d6")]
    [InlineData(11, "3d6")]
    [InlineData(15, "3d6")]
    [InlineData(16, "4d6")]
    [InlineData(20, "4d6")]
    public void DiceForLevel_maps_tier(int level, string dice) =>
        BreathWeaponRules.DiceForLevel(level).Should().Be(dice);

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(9, 4)]
    [InlineData(13, 5)]
    [InlineData(17, 6)]
    [InlineData(20, 6)]
    public void ProficiencyBonus_by_level(int level, int prof) =>
        BreathWeaponRules.ProficiencyBonus(level).Should().Be(prof);

    [Theory]
    [InlineData(11, 3, 15)]  // 8 + 4 + 3
    [InlineData(3, 2, 12)]   // 8 + 2 + 2
    [InlineData(17, 5, 19)]  // 8 + 6 + 5
    public void SaveDc_is_8_plus_prof_plus_conmod(int level, int conMod, int dc) =>
        BreathWeaponRules.SaveDc(level, conMod).Should().Be(dc);
}