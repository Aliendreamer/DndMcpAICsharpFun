using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Seam over <see cref="GroundingCascade"/> so consumers (e.g. the backlog
/// <c>RegroundService</c>) can be exercised with a fake that returns scripted verdicts
/// deterministically, without going through the real Tier 1/2 infrastructure.
/// </summary>
public interface IGroundingCascade
{
    Task<GroundingVerdict> GradeAsync(
        EntityEnvelope entity, string sourceProse, bool judgeEnabled, CancellationToken ct);
}
