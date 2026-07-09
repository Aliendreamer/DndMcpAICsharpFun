using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class GroundingCascadeTests
{
    private sealed class FakeTier1Grounding(Tier1Result result) : ITier1Grounding
    {
        public int CallCount { get; private set; }

        public Task<Tier1Result> GroundAsync(string entityText, string sourceBook, int? page, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeGroundingJudge(bool? verdict) : IGroundingJudge
    {
        public int CallCount { get; private set; }

        public Task<bool?> AreFieldsSupportedAsync(EntityEnvelope entity, string sourceProse, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(verdict);
        }
    }

    private static EntityEnvelope MakeEnvelope(string fieldsJson) => new(
        Id: "phb.spell.fireball",
        Type: EntityType.Spell,
        Name: "Fireball",
        SourceBook: "PHB",
        Edition: "Edition2014",
        Page: 241,
        FirstAppearedIn: new FirstAppearance("PHB", "Edition2014", 241),
        RevisedIn: [],
        SettingTags: [],
        CanonicalText: "Fireball hurls a fiery explosion.",
        Fields: JsonDocument.Parse(fieldsJson).RootElement.Clone());

    [Fact]
    public async Task Tier0FieldPresent_ShortCircuits_NoTier1OrJudgeCall()
    {
        var entity = MakeEnvelope("""{"damage":"8d6 fire"}""");
        const string sourceProse = "The spell deals 8d6 fire damage to everything in the area.";
        var tier1 = new FakeTier1Grounding(new Tier1Result(BelowFloor: false, Score: 0.9));
        var judge = new FakeGroundingJudge(true);
        var sut = new GroundingCascade(tier1, judge);

        var verdict = await sut.GradeAsync(entity, sourceProse, judgeEnabled: true, CancellationToken.None);

        verdict.Status.Should().Be(GroundingStatus.Grounded);
        verdict.DecidedByTier.Should().Be(0);
        tier1.CallCount.Should().Be(0);
        judge.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Tier1BelowFloor_JudgeEnabled_IsUngrounded_JudgeNotCalled()
    {
        var entity = MakeEnvelope("""{"damage":"unmatched-fabricated-value"}""");
        const string sourceProse = "This prose does not support the fabricated field at all.";
        var tier1 = new FakeTier1Grounding(new Tier1Result(BelowFloor: true, Score: 0.1));
        var judge = new FakeGroundingJudge(true);
        var sut = new GroundingCascade(tier1, judge);

        var verdict = await sut.GradeAsync(entity, sourceProse, judgeEnabled: true, CancellationToken.None);

        verdict.Status.Should().Be(GroundingStatus.Ungrounded);
        verdict.DecidedByTier.Should().Be(1);
        tier1.CallCount.Should().Be(1);
        judge.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Tier1AboveFloor_JudgeEnabled_JudgeYes_IsGrounded_JudgeCalledOnce()
    {
        var entity = MakeEnvelope("""{"damage":"unmatched-fabricated-value"}""");
        const string sourceProse = "This prose does not support the fabricated field at all.";
        var tier1 = new FakeTier1Grounding(new Tier1Result(BelowFloor: false, Score: 0.8));
        var judge = new FakeGroundingJudge(true);
        var sut = new GroundingCascade(tier1, judge);

        var verdict = await sut.GradeAsync(entity, sourceProse, judgeEnabled: true, CancellationToken.None);

        verdict.Status.Should().Be(GroundingStatus.Grounded);
        verdict.DecidedByTier.Should().Be(2);
        tier1.CallCount.Should().Be(1);
        judge.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Tier1AboveFloor_JudgeEnabled_JudgeNo_IsUngrounded()
    {
        var entity = MakeEnvelope("""{"damage":"unmatched-fabricated-value"}""");
        const string sourceProse = "This prose does not support the fabricated field at all.";
        var tier1 = new FakeTier1Grounding(new Tier1Result(BelowFloor: false, Score: 0.8));
        var judge = new FakeGroundingJudge(false);
        var sut = new GroundingCascade(tier1, judge);

        var verdict = await sut.GradeAsync(entity, sourceProse, judgeEnabled: true, CancellationToken.None);

        verdict.Status.Should().Be(GroundingStatus.Ungrounded);
        verdict.DecidedByTier.Should().Be(2);
        judge.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Tier0Fails_JudgeDisabled_SkipsTier1Entirely_IsUncertain()
    {
        // M-1 optimization: with the judge off, Tier 1's score can never change the verdict
        // (GroundingCombiner always returns Uncertain for a Tier-0 failure when judgeEnabled is
        // false), so the embed + Qdrant round trip must never happen in this case.
        var entity = MakeEnvelope("""{"damage":"unmatched-fabricated-value"}""");
        const string sourceProse = "This prose does not support the fabricated field at all.";
        var tier1 = new FakeTier1Grounding(new Tier1Result(BelowFloor: false, Score: 0.8));
        var judge = new FakeGroundingJudge(true);
        var sut = new GroundingCascade(tier1, judge);

        var verdict = await sut.GradeAsync(entity, sourceProse, judgeEnabled: false, CancellationToken.None);

        verdict.Status.Should().Be(GroundingStatus.Uncertain);
        tier1.CallCount.Should().Be(0, "Tier 1 must be skipped entirely when the judge is disabled");
        judge.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Tier1AboveFloor_JudgeEnabled_JudgeReturnsNull_IsUncertain_NotUngrounded()
    {
        // I-2: a judge that could not decide (transient failure or unparsable reply) must yield
        // Uncertain, never a confirmed fabrication (Ungrounded).
        var entity = MakeEnvelope("""{"damage":"unmatched-fabricated-value"}""");
        const string sourceProse = "This prose does not support the fabricated field at all.";
        var tier1 = new FakeTier1Grounding(new Tier1Result(BelowFloor: false, Score: 0.8));
        var judge = new FakeGroundingJudge(null);
        var sut = new GroundingCascade(tier1, judge);

        var verdict = await sut.GradeAsync(entity, sourceProse, judgeEnabled: true, CancellationToken.None);

        verdict.Status.Should().Be(GroundingStatus.Uncertain);
        judge.CallCount.Should().Be(1);
    }
}