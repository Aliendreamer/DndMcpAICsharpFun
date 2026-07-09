using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Composes the three grounding tiers (parent prose-grounded-knowledge-model design.md §G) into a
/// single verdict, short-circuiting to the cheapest tier that can decide: Tier 0 (field-text match)
/// skips Tier 1/2 entirely when it grounds; Tier 2 (LLM judge) only runs when Tier 1 is above its
/// similarity floor and the judge is enabled.
/// </summary>
public sealed class GroundingCascade(ITier1Grounding tier1, IGroundingJudge judge) : IGroundingCascade
{
    public async Task<GroundingVerdict> GradeAsync(
        EntityEnvelope entity, string sourceProse, bool judgeEnabled, CancellationToken ct)
    {
        if (Tier0FieldGrounding.HasAnyFieldGrounded(entity.Fields, sourceProse))
            return GroundingCombiner.Combine(tier0Grounded: true, tier1: null, judgeEnabled, tier2Grounded: null);

        // With the judge disabled, Tier 1's score can never change the verdict — GroundingCombiner
        // always yields Uncertain for a Tier-0 failure when judgeEnabled is false, regardless of
        // the Tier 1 score. Skip the embed + Qdrant round trip entirely in that case (both the
        // extraction hot path and the reground fast pass benefit).
        if (!judgeEnabled)
            return GroundingCombiner.Combine(tier0Grounded: false, tier1: null, judgeEnabled: false, tier2Grounded: null);

        var t1 = await tier1.GroundAsync(EntityTextFor(entity), entity.SourceBook, entity.Page, ct);

        bool? tier2Grounded = t1.BelowFloor
            ? null
            : await judge.AreFieldsSupportedAsync(entity, sourceProse, ct);

        return GroundingCombiner.Combine(tier0Grounded: false, t1, judgeEnabled: true, tier2Grounded);
    }

    private static string EntityTextFor(EntityEnvelope entity) =>
        string.IsNullOrEmpty(entity.CanonicalText) ? entity.Fields.GetRawText() : entity.CanonicalText;
}