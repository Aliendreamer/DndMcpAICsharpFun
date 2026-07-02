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
            return (BuildTypedEnvelope(id, resolution.ForcedType, displayName, sourceBook, edition, candidate, forcedClean, forcedConfidence), null);
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
                return (DeclinedEnvelope(id, candidate, displayName, sourceBook, edition, result.DeclineReason), null);

            default:
                return (BuildTypedEnvelope(id, result.Type, displayName, sourceBook, edition, candidate, result.Fields, result.Confidence), null);
        }
    }

    // Builds a typed entity envelope, deriving the disposition from grounding + name/confidence.
    // The Id keeps the keyword-primary type for stable checkpoint/resume identity (design.md §F);
    // the authoritative type is the Type field.
    private EntityEnvelope BuildTypedEnvelope(
        string id, EntityType type, string displayName, string sourceBook, string edition,
        EntityCandidate candidate, JsonElement fields, string? confidence)
    {
        var grounded = HasGroundedContent(fields, candidate.Text);
        var disposition = ExtractionDispositionPolicy.Derive(grounded, displayName, confidence);
        return new EntityEnvelope(
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
            NeedsReview:     disposition != EntityDisposition.Accepted,
            Disposition:     disposition);
    }

    private static EntityEnvelope DeclinedEnvelope(
        string id, EntityCandidate candidate, string displayName,
        string sourceBook, string edition, string? reason)
    {
        using var empty = JsonDocument.Parse("{}");
        return new EntityEnvelope(
            Id:              id,
            Type:            candidate.Type,
            Name:            displayName,
            SourceBook:      sourceBook,
            Edition:         edition,
            Page:            candidate.Page,
            FirstAppearedIn: new FirstAppearance(sourceBook, edition, candidate.Page),
            RevisedIn:       Array.Empty<Revision>(),
            SettingTags:     Array.Empty<string>(),
            CanonicalText:   reason ?? string.Empty,
            Fields:          empty.RootElement.Clone(),
            NeedsReview:     true,
            Disposition:     EntityDisposition.Declined);
    }

    // Tier-0 grounding over the emitted fields: true when at least one significant string value
    // grounds against the source prose. Pure fabrication / empty output (e.g. zeroed stat blocks)
    // grounds nothing -> NeedsReview.
    private static bool HasGroundedContent(JsonElement fields, string sourceText)
    {
        foreach (var value in EnumerateStringValues(fields))
            if (Tier0FieldGrounding.IsTextGrounded(value, sourceText))
                return true;
        return false;
    }

    private static IEnumerable<string> EnumerateStringValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var s = element.GetString();
                if (!string.IsNullOrWhiteSpace(s)) yield return s;
                break;
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                    foreach (var v in EnumerateStringValues(p.Value))
                        yield return v;
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var v in EnumerateStringValues(item))
                        yield return v;
                break;
        }
    }

    // Title-case clean all-caps display names before they become entity names + feed the heuristic.
    private static string NormalizeDisplayName(string displayName)
        => EntityNameNormalizer.TryNormalizeHeading(displayName, out var n) ? n : displayName;
}
