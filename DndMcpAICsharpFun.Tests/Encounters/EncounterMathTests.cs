using DndMcpAICsharpFun.Domain;              // DndVersion
using DndMcpAICsharpFun.Features.Encounters;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Encounters;

public sealed class EncounterMathTests
{
    [Theory]
    [InlineData(0, 10)] [InlineData(0.25, 50)] [InlineData(1, 200)]
    [InlineData(5, 1800)] [InlineData(10, 5900)] [InlineData(20, 25000)] [InlineData(30, 155000)]
    public void CrToXp_matches_standard_table(double cr, int xp) =>
        EncounterMath.CrToXp(cr).Should().Be(xp);

    [Fact]
    public void Party_thresholds_2014_sum_over_party()
    {
        // 4× level-5 PCs: per-char Easy/Med/Hard/Deadly = 250/500/750/1100  (DMG p.82)
        var b = EncounterMath.PartyBudget(new[] { 5, 5, 5, 5 }, DndVersion.Edition2014);
        b.Should().Equal(1000, 2000, 3000, 4400);
    }

    [Fact]
    public void Party_budget_2024_sum_over_party()
    {
        // 4× level-5 PCs: per-char Low/Mod/High = 500/750/1100 (2024 DMG)
        var b = EncounterMath.PartyBudget(new[] { 5, 5, 5, 5 }, DndVersion.Edition2024);
        b.Should().Equal(2000, 3000, 4400);
    }

    [Theory]
    [InlineData(1, 4, 1.0)] [InlineData(2, 4, 1.5)] [InlineData(4, 4, 2.0)]
    [InlineData(8, 4, 2.5)] [InlineData(12, 4, 3.0)] [InlineData(15, 4, 4.0)]
    public void Multiplier_2014_by_count(int count, int party, double mult) =>
        EncounterMath.Multiplier(count, party, DndVersion.Edition2014).Should().Be(mult);

    [Fact]
    public void Multiplier_shifts_up_for_small_party_and_down_for_large()
    {
        EncounterMath.Multiplier(2, 2, DndVersion.Edition2014).Should().Be(2.0);  // <3 PCs: 1.5 -> next step 2.0
        EncounterMath.Multiplier(2, 6, DndVersion.Edition2014).Should().Be(1.0);  // >=6 PCs: 1.5 -> prev step 1.0
    }

    [Fact]
    public void Multiplier_2024_is_always_one() =>
        EncounterMath.Multiplier(5, 4, DndVersion.Edition2024).Should().Be(1.0);

    [Fact]
    public void Classify_2014_uses_multiplier()
    {
        // party 4×L5 (thresholds Easy1000/Med2000/Hard3000/Deadly4400).
        // Three CR-3 monsters = 2100 XP, count 3 -> ×2 = 4200 adjusted -> ≥Hard(3000), <Deadly(4400) -> Hard.
        EncounterMath.Classify(2100, new[] { 5, 5, 5, 5 }, monsterCount: 3, DndVersion.Edition2014)
            .Should().Be(Difficulty.Hard);
    }

    [Fact]
    public void Classify_2024_no_multiplier()
    {
        // party 4×L5 (budgets 2000/3000/4400). 1400 raw XP -> below Low(2000) -> Trivial.
        EncounterMath.Classify(1400, new[] { 5, 5, 5, 5 }, monsterCount: 2, DndVersion.Edition2024)
            .Should().Be(Difficulty.Trivial);
    }

    [Fact]
    public void Classify_boundary_just_below_is_lower_band()
    {
        // 2024, party 4×L5: exactly Moderate(3000) => Medium; one below => Easy.
        EncounterMath.Classify(3000, new[] { 5,5,5,5 }, 1, DndVersion.Edition2024).Should().Be(Difficulty.Medium);
        EncounterMath.Classify(2999, new[] { 5,5,5,5 }, 1, DndVersion.Edition2024).Should().Be(Difficulty.Easy);
    }
}
