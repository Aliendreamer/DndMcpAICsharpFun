using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Tier 2 of the grounding cascade: an LLM judge asked strictly whether an entity's emitted
/// fields are supported by its source prose. Invoked only on the residual the cheaper Tier 0/1
/// checks could not resolve.
/// </summary>
public interface IGroundingJudge
{
    /// <summary>
    /// Tri-state verdict: <c>true</c> = fields are supported by the prose, <c>false</c> = fields
    /// are not supported (a confirmed fabrication), <c>null</c> = the judge could not decide
    /// (transient failure or an unparsable reply) — callers must treat this as Uncertain, never
    /// as a confirmed fabrication.
    /// </summary>
    Task<bool?> AreFieldsSupportedAsync(EntityEnvelope entity, string sourceProse, CancellationToken ct);
}