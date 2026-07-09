using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Tier 2 of the grounding cascade: an LLM judge asked strictly whether an entity's emitted
/// fields are supported by its source prose. Invoked only on the residual the cheaper Tier 0/1
/// checks could not resolve.
/// </summary>
public interface IGroundingJudge
{
    Task<bool> AreFieldsSupportedAsync(EntityEnvelope entity, string sourceProse, CancellationToken ct);
}
