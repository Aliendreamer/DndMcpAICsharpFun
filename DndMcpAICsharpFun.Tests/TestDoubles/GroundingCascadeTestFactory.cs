using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using NSubstitute;

namespace DndMcpAICsharpFun.Tests.TestDoubles;

/// <summary>
/// Builds a <see cref="GroundingCascade"/> for tests that exercise the extraction pipeline without
/// caring about Tier 1/2 grounding specifically. Tier 0 (candidate.Text match) still runs for real
/// inside the cascade; these fakes only stand in for the escalation path, which extraction-time
/// callers keep disabled (<c>judgeEnabled: false</c>) so the judge is never expected to run.
/// </summary>
public static class GroundingCascadeTestFactory
{
    public static GroundingCascade Inert()
    {
        var tier1 = Substitute.For<ITier1Grounding>();
        tier1.GroundAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new Tier1Result(BelowFloor: false, Score: 0.5));

        var judge = Substitute.For<IGroundingJudge>();
        judge.AreFieldsSupportedAsync(Arg.Any<EntityEnvelope>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        return new GroundingCascade(tier1, judge);
    }
}
