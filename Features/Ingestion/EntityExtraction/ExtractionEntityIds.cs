using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Shared id/naming helpers used by the extraction orchestrator, the candidate builder and the
/// per-candidate runner. <see cref="RecordedEntityId"/> is the SINGLE source of truth for the id
/// under which an entity (or its error) is recorded, so the orchestrator's retry-set / checkpoint
/// membership tests and the runner's extraction always agree on the same id (design.md §F).
/// Do not fork this logic.
/// </summary>
internal static class ExtractionEntityIds
{
    // The book identifier used for slug/id derivation: the 5etools source key when present
    // (mapped to a canonical slug like phb14 by EntityIdSlug), else the display name.
    public static string BookKey(DndMcpAICsharpFun.Domain.IngestionRecord record) =>
        record.FivetoolsSourceKey ?? record.DisplayName;

    // The id under which an entity (or its error) is recorded: the canonical 5etools id when the
    // name matches the index, else the raw heading id. Every membership test (checkpoint doneIds,
    // errors-only retrySet) and the runner's ExtractOneAsync must agree on this id, or matched
    // candidates are silently re-extracted (resume) or skipped (retry).
    public static string RecordedEntityId(
        DndMcpAICsharpFun.Domain.IngestionRecord record,
        EntityCandidate candidate,
        EntityNameMatcher? matcher,
        bool isOfficial = false)
    {
        var resolution = DeterministicTypeResolver.Resolve(candidate, matcher, isOfficial);
        return resolution.Outcome == DeterministicOutcome.ForceType && resolution.CanonicalName is { } cn
            ? EntityIdSlug.For(BookKey(record), resolution.ForcedType, cn)
            : EntityIdSlug.For(BookKey(record), candidate.Type, candidate.DisplayName);
    }
}
