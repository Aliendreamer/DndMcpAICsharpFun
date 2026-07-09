using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class GroundingCombinerTests
{
    [Fact]
    public void Tier0_confirm_is_grounded_tier0()
    {
        var v = GroundingCombiner.Combine(tier0Grounded: true, tier1: null, judgeEnabled: false, tier2Grounded: null);
        v.Status.Should().Be(GroundingStatus.Grounded);
        v.DecidedByTier.Should().Be(0);
    }

    [Fact]
    public void Tier1_below_floor_with_judge_is_ungrounded_tier1()
    {
        var v = GroundingCombiner.Combine(false, new Tier1Result(BelowFloor: true, Score: 0.1), judgeEnabled: true, tier2Grounded: null);
        v.Status.Should().Be(GroundingStatus.Ungrounded);
        v.DecidedByTier.Should().Be(1);
    }

    [Fact]
    public void Tier1_below_floor_without_judge_is_uncertain()
    {
        var v = GroundingCombiner.Combine(false, new Tier1Result(true, 0.1), judgeEnabled: false, tier2Grounded: null);
        v.Status.Should().Be(GroundingStatus.Uncertain);
    }

    [Fact]
    public void Tier1_above_floor_escalates_to_tier2_grounded()
    {
        var v = GroundingCombiner.Combine(false, new Tier1Result(BelowFloor: false, Score: 0.8), judgeEnabled: true, tier2Grounded: true);
        v.Status.Should().Be(GroundingStatus.Grounded);
        v.DecidedByTier.Should().Be(2);
    }

    [Fact]
    public void Tier1_above_floor_escalates_to_tier2_ungrounded()
    {
        var v = GroundingCombiner.Combine(false, new Tier1Result(false, 0.8), judgeEnabled: true, tier2Grounded: false);
        v.Status.Should().Be(GroundingStatus.Ungrounded);
        v.DecidedByTier.Should().Be(2);
    }

    [Fact]
    public void Tier1_above_floor_no_judge_is_uncertain()
    {
        var v = GroundingCombiner.Combine(false, new Tier1Result(false, 0.8), judgeEnabled: false, tier2Grounded: null);
        v.Status.Should().Be(GroundingStatus.Uncertain);
    }

    [Fact]
    public void Tier1_never_grounds_on_its_own()
    {
        // above floor, judge enabled but tier2 not yet decided -> must NOT be Grounded by tier1
        var v = GroundingCombiner.Combine(false, new Tier1Result(false, 0.99), judgeEnabled: true, tier2Grounded: null);
        v.Status.Should().NotBe(GroundingStatus.Grounded);
    }
}
