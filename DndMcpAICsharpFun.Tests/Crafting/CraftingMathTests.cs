using DndMcpAICsharpFun.Features.Crafting;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Crafting;

public sealed class CraftingMathTests
{
    [Fact]
    public void CraftNonmagical_PlateArmor_1500gp()
    {
        var r = CraftingMath.CraftNonmagical(1500);
        r.MaterialsGp.Should().Be(750);
        r.TotalWorkweeks.Should().Be(30.0);
        r.PerCrafterWorkweeks.Should().Be(30.0);
        r.Days.Should().Be(150);
    }

    [Fact]
    public void CraftNonmagical_MultipleCrafters_DivideTime()
    {
        var r = CraftingMath.CraftNonmagical(1500, crafters: 3);
        r.MaterialsGp.Should().Be(750);
        r.TotalWorkweeks.Should().Be(30.0);
        r.PerCrafterWorkweeks.Should().Be(10.0);
        r.Days.Should().Be(50);
    }

    [Fact]
    public void CraftNonmagical_FractionalWorkweeks_Preserved()
    {
        var r = CraftingMath.CraftNonmagical(175);
        r.TotalWorkweeks.Should().Be(3.5);
        r.Days.Should().Be(18); // ceil(17.5)
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void CraftNonmagical_NonPositiveValue_Throws(int value)
    {
        var act = () => CraftingMath.CraftNonmagical(value);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CraftNonmagical_ZeroCrafters_ClampedToOne()
    {
        var r = CraftingMath.CraftNonmagical(1500, crafters: 0);
        r.PerCrafterWorkweeks.Should().Be(30.0);
        r.Days.Should().Be(150);
    }

    [Theory]
    [InlineData(Rarity.Common, 1, 50)]
    [InlineData(Rarity.Uncommon, 2, 200)]
    [InlineData(Rarity.Rare, 10, 2000)]
    [InlineData(Rarity.VeryRare, 25, 20000)]
    [InlineData(Rarity.Legendary, 50, 100000)]
    public void CraftMagicItem_RarityTable(Rarity rarity, int workweeks, int gold)
    {
        var r = CraftingMath.CraftMagicItem(rarity);
        r.Rarity.Should().Be(rarity);
        r.Workweeks.Should().Be(workweeks);
        r.GoldCostGp.Should().Be(gold);
    }
}
