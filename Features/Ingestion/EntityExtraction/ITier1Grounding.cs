namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Tier 1 of the grounding cascade: embedding-similarity check of the entity text against the
/// entity's own source book in <c>dnd_blocks</c>. Extracted as an interface so
/// <see cref="GroundingCascade"/> can be exercised with a fake in tests.
/// </summary>
public interface ITier1Grounding
{
    Task<Tier1Result> GroundAsync(string entityText, string sourceBook, int? page, CancellationToken ct);
}
