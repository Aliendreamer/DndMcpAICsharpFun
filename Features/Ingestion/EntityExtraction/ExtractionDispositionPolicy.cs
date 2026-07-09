using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Derives a typed entity's <see cref="EntityDisposition"/> from the grounding-gate result plus the
/// legacy name/confidence heuristics (capability <c>extraction-disposition</c>). The decisive change
/// from the old <c>needsReview</c> boolean: a confident, well-cased, but UNGROUNDED extraction is NOT
/// accepted — grounding is checked first, independent of the model's self-report.
/// </summary>
public static class ExtractionDispositionPolicy
{
    public static EntityDisposition Derive(bool grounded, string name, string? confidence)
    {
        if (!grounded) return EntityDisposition.NeedsReview;
        return DeriveGroundedGate(name, confidence);
    }

    /// <summary>
    /// Verdict-based overload backed by the shared <see cref="GroundingCascade"/> (Task 6): an
    /// <see cref="GroundingStatus.Ungrounded"/> verdict (Tier-2-judge-confirmed fabrication) maps to
    /// its own <see cref="EntityDisposition.Ungrounded"/> rather than the softer NeedsReview; an
    /// <see cref="GroundingStatus.Uncertain"/> verdict (no judge verdict reached) falls back to
    /// NeedsReview; a <see cref="GroundingStatus.Grounded"/> verdict runs the existing name/confidence
    /// gate, identical to the legacy <c>grounded == true</c> path above.
    /// </summary>
    public static EntityDisposition Derive(GroundingVerdict verdict, string name, string? confidence) =>
        verdict.Status switch
        {
            GroundingStatus.Ungrounded => EntityDisposition.Ungrounded,
            GroundingStatus.Uncertain => EntityDisposition.NeedsReview,
            _ => DeriveGroundedGate(name, confidence),
        };

    private static EntityDisposition DeriveGroundedGate(string name, string? confidence) =>
        ExtractionNeedsReview.Derive(name, confidence) ? EntityDisposition.NeedsReview : EntityDisposition.Accepted;
}