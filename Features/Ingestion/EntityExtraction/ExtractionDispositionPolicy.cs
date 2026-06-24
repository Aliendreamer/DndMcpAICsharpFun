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
        if (ExtractionNeedsReview.Derive(name, confidence)) return EntityDisposition.NeedsReview;
        return EntityDisposition.Accepted;
    }
}
