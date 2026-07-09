namespace DndMcpAICsharpFun.Domain.Entities;

/// <summary>
/// Per-candidate extraction disposition — the trust signal that supersedes the legacy
/// <c>needsReview</c> boolean (parent prose-grounded-knowledge-model design.md §I,
/// capability <c>extraction-disposition</c>). <c>Accepted</c> is 0 so canonical entities
/// written before this change (which lack the field) deserialize as <c>Accepted</c>,
/// preserving prior human-reviewed output.
/// </summary>
public enum EntityDisposition
{
    /// <summary>Typed, grounded, and not flagged — eligible for <c>dnd_entities</c>.</summary>
    Accepted = 0,

    /// <summary>Emitted but ungrounded, low/medium confidence, OCR-noisy name, or an ambiguous decline.</summary>
    NeedsReview = 1,

    /// <summary>The model chose the <c>none</c> branch on clearly non-entity content. Recorded for audit, not ingested.</summary>
    Declined = 2,

    /// <summary>No valid output after the retry budget.</summary>
    Failed = 3,

    /// <summary>A Tier-2-judge-confirmed ungrounded fabrication: the model emitted an entity but
    /// its fields are not supported by the source prose. Excluded from <c>dnd_entities</c>,
    /// retained in canonical for audit. Distinct from a model-chosen <c>Declined</c>.</summary>
    Ungrounded = 4,
}