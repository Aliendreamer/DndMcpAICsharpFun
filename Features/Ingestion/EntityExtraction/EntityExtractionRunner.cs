using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Runs the LLM-backed extraction for a SINGLE candidate: deterministic type resolution,
/// forced-type or content-first union extraction, envelope construction, and Tier-0 grounding.
/// Shared by the orchestrator's full and errors-only run modes; produces exactly one of an
/// <see cref="EntityEnvelope"/> or an <see cref="ExtractionErrorEntry"/> per candidate.
/// </summary>
public sealed class EntityExtractionRunner(
    CandidateExtractor candidateExtractor,
    ILogger<EntityExtractionRunner> logger,
    IGroundingCascade cascade,
    EntityNameMatcher? matcher = null)
{
    private readonly EntityNameMatcher? _matcher = matcher;

    /// <summary>
    /// Shared per-candidate extraction pipeline used by both full and errors-only run modes.
    /// Returns either a successfully built <see cref="EntityEnvelope"/> or an <see cref="ExtractionErrorEntry"/>;
    /// exactly one of the two tuple members is non-null. The <paramref name="id"/> must have been
    /// computed via <see cref="ExtractionEntityIds.RecordedEntityId"/> so it matches the id used for
    /// checkpoint/retry membership tests.
    /// </summary>
    public async Task<(EntityEnvelope? Envelope, ExtractionErrorEntry? Error)> ExtractOneAsync(
        DndMcpAICsharpFun.Domain.IngestionRecord record,
        EntityCandidate candidate,
        string id,
        string sourceBook,
        string edition,
        Dictionary<EntityType, JsonElement> schemas,
        CancellationToken ct,
        bool isOfficial = false)
    {
        // Offer only prior types that actually have a schema. If none do, it is a configuration
        // problem (no_schema), recorded without an LLM call.
        var availablePrior = candidate.TypePrior.Where(schemas.ContainsKey).ToList();
        if (availablePrior.Count == 0)
        {
            logger.LogWarning(
                "No schema for any prior type of candidate {Name}; recording no_schema",
                candidate.DisplayName);
            return (null, new ExtractionErrorEntry(
                SourceEntityId: id,
                FieldPath: "(type)",
                MissingTargetId: string.Empty,
                ErrorKind: "no_schema",
                Detail: $"No JSON schema for any prior type of {candidate.DisplayName}"));
        }

        var displayName = NormalizeDisplayName(candidate.DisplayName);

        // Deterministic type resolution before the content-first union. Drop/Decline candidates are
        // handled upstream; only ForceType/Defer reach here. A forced type extracts with that
        // type's schema directly; Defer uses the union below.
        var resolution = DeterministicTypeResolver.Resolve(candidate, _matcher, isOfficial);

        // When the 5etools matcher supplies a canonical name, use it for both the entity's
        // display name and ID so the canonical JSON reflects the authoritative 5etools spelling
        // rather than the raw (often all-caps) heading text.
        if (resolution.Outcome == DeterministicOutcome.ForceType && resolution.CanonicalName is { } cn)
        {
            displayName = NormalizeDisplayName(cn);
            id = EntityIdSlug.For(ExtractionEntityIds.BookKey(record), resolution.ForcedType, cn);
        }

        if (resolution.Outcome == DeterministicOutcome.ForceType &&
            schemas.TryGetValue(resolution.ForcedType, out var forcedSchema))
        {
            var (forcedFields, forcedError) = await candidateExtractor.ExtractFieldsAsync(
                record, candidate with { Type = resolution.ForcedType }, forcedSchema, ct);
            if (forcedFields is null)
            {
                logger.LogWarning(
                    "Forced {Type} extraction failed for '{Name}' (page {Page}): {Error}",
                    resolution.ForcedType, candidate.DisplayName, candidate.Page, forcedError);
                return (null, new ExtractionErrorEntry(
                    SourceEntityId: id, FieldPath: "(extraction)", MissingTargetId: string.Empty,
                    ErrorKind: "extraction_failure", Detail: forcedError));
            }

            var forcedConfidence = forcedFields.Value.TryGetProperty("confidence", out var fcp) ? fcp.GetString() : null;
            var forcedClean = CandidateExtractor.StripConfidence(forcedFields.Value);
            return (await BuildTypedEnvelope(id, resolution.ForcedType, displayName, sourceBook, edition, candidate, forcedClean, forcedConfidence, ct), null);
        }

        var result = await candidateExtractor.ExtractUnionAsync(record, candidate, availablePrior, schemas, ct);

        switch (result.Outcome)
        {
            case UnionOutcome.Failed:
                logger.LogWarning(
                    "Union extraction failed for '{Name}' (page {Page}): {Error}",
                    candidate.DisplayName, candidate.Page, result.ErrorMessage);
                return (null, new ExtractionErrorEntry(
                    SourceEntityId: id,
                    FieldPath: "(extraction)",
                    MissingTargetId: string.Empty,
                    ErrorKind: "extraction_failure",
                    Detail: result.ErrorMessage));

            case UnionOutcome.Declined:
                // The model chose the `none` branch — it could not confidently extract an entity.
                // Record it as a re-triable extraction decline (errors.json) instead of persisting an
                // empty entity whose canonicalText would be the model's classification reasoning
                // (the reasoning-leak defect). An errorsOnly retry re-processes it.
                return (null, new ExtractionErrorEntry(
                    SourceEntityId: id,
                    FieldPath: "(extraction)",
                    MissingTargetId: string.Empty,
                    ErrorKind: "extraction_declined",
                    Detail: result.DeclineReason ?? "model declined (entityType:none)"));

            default:
                return (await BuildTypedEnvelope(id, result.Type, displayName, sourceBook, edition, candidate, result.Fields, result.Confidence, ct), null);
        }
    }

    // Builds a typed entity envelope, deriving the disposition from the shared GroundingCascade
    // (Tier 0 field-text match, escalating to Tier 1/2 only when Tier 0 fails) plus name/confidence.
    // The Id keeps the keyword-primary type for stable checkpoint/resume identity (design.md §F);
    // the authoritative type is the Type field.
    private async Task<EntityEnvelope> BuildTypedEnvelope(
        string id, EntityType type, string displayName, string sourceBook, string edition,
        EntityCandidate candidate, JsonElement fields, string? confidence, CancellationToken ct)
    {
        var provisional = new EntityEnvelope(
            Id:              id,
            Type:            type,
            Name:            displayName,
            SourceBook:      sourceBook,
            Edition:         edition,
            Page:            candidate.Page,
            FirstAppearedIn: new FirstAppearance(sourceBook, edition, candidate.Page),
            RevisedIn:       Array.Empty<Revision>(),
            SettingTags:     Array.Empty<string>(),
            CanonicalText:   string.Empty,
            Fields:          fields,
            NeedsReview:     false,
            Disposition:     EntityDisposition.Accepted);

        // judgeEnabled is hardcoded false here: normal extraction runs keep Tier 2 (the LLM judge)
        // off. With the judge disabled, GroundingCascade.GradeAsync also skips Tier 1 (the
        // embed + Qdrant round trip) whenever Tier 0 fails, since Tier 1 alone can never change a
        // judge-off verdict — so the common extraction path only pays Tier 0's cost, cheaper than
        // the pre-cascade baseline, not merely "unchanged" from it. The backlog grounding pass
        // (Task 7) re-grades existing entities with judgeEnabled: true threaded through from its
        // own run options, which is what actually exercises Tier 1/2.
        var verdict = await cascade.GradeAsync(provisional, candidate.Text, judgeEnabled: false, ct);
        var disposition = ExtractionDispositionPolicy.Derive(verdict, displayName, confidence);

        return provisional with
        {
            NeedsReview = disposition != EntityDisposition.Accepted,
            Disposition = disposition,
        };
    }

    // Title-case clean all-caps display names before they become entity names + feed the heuristic.
    private static string NormalizeDisplayName(string displayName)
        => EntityNameNormalizer.TryNormalizeHeading(displayName, out var n) ? n : displayName;
}
