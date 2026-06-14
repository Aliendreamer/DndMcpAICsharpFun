namespace DndMcpAICsharpFun.Features.Ingestion.Entities;

public interface IEntityIngestionOrchestrator
{
    Task<EntityIngestionResult> IngestEntitiesAsync(int bookId, CancellationToken ct = default);
}

/// <summary>Result of a single entity-ingest run, including 5etools enrichment coverage counts.</summary>
/// <param name="TotalEntities">Number of entities attempted (rendered + embedded).</param>
/// <param name="MatchedFivetools">Entities that had a matching 5etools file record.</param>
/// <param name="Unmatched">Entities with no 5etools match (ingested without enrichment).</param>
public sealed record EntityIngestionResult(
    int TotalEntities,
    int MatchedFivetools,
    int Unmatched)
{
    /// <summary>Alias for <see cref="MatchedFivetools"/> — all matched entities were enriched.</summary>
    public int Enriched => MatchedFivetools;
}
