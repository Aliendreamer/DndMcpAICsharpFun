namespace DndMcpAICsharpFun.Features.Ingestion.Entities;

public interface IEntityIngestionOrchestrator
{
    Task<EntityIngestionResult> IngestEntitiesAsync(int bookId, CancellationToken ct = default);

    /// <summary>
    /// Targeted single-entity re-index: loads only the entity with <paramref name="entityId"/>
    /// from the book's canonical JSON, runs merge → render → embed, then upserts exactly that
    /// one <see cref="EntityPoint"/> (using the book's FileHash).
    /// <para>
    /// This method MUST NOT call <c>DeleteByFileHashExceptAsync</c> — it leaves all other
    /// points untouched.
    /// </para>
    /// </summary>
    Task ReindexEntityAsync(int bookId, string entityId, CancellationToken ct = default);
}

/// <summary>Result of a single entity-ingest run, including 5etools enrichment coverage counts.</summary>
/// <param name="TotalEntities">Number of entities attempted (rendered + embedded).</param>
/// <param name="MatchedFivetools">Entities that had a matching 5etools file record.</param>
/// <param name="Unmatched">Entities with no 5etools match (ingested without enrichment).</param>
public sealed record EntityIngestionResult(
    int TotalEntities,
    int MatchedFivetools,
    int Unmatched);
