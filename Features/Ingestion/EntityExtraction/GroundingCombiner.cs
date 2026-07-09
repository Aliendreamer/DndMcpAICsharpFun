namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>Pure combination of the three grounding tiers into a verdict. No I/O.</summary>
public static class GroundingCombiner
{
    public static GroundingVerdict Combine(
        bool tier0Grounded, Tier1Result? tier1, bool judgeEnabled, bool? tier2Grounded)
    {
        if (tier0Grounded) return new(GroundingStatus.Grounded, 0, 1.0);
        if (tier1 is not { } t1) return new(GroundingStatus.Uncertain, 1, 0.0);

        // Tier 2 decided (only reachable when judge ran).
        if (tier2Grounded is { } judged)
            return new(judged ? GroundingStatus.Grounded : GroundingStatus.Ungrounded, 2, t1.Score);

        // No judge verdict. Tier 1 alone: reject only if below floor AND judge enabled; else uncertain.
        if (t1.BelowFloor && judgeEnabled)
            return new(GroundingStatus.Ungrounded, 1, t1.Score);

        return new(GroundingStatus.Uncertain, 1, t1.Score);
    }
}