using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Re-offers a declined candidate a recovery-framed union call restricted to [Rule, Lore] (plus the
/// implicit none branch), so genuine rule/lore prose that fell through the primary type prior is not
/// silently lost. The runner applies the same deterministic type resolution, forced/union extraction,
/// and grounding cascade as the primary path — a returned envelope has already grounded and been
/// disposed by that cascade; a null result means the candidate stays declined (none pick, extraction
/// failure, or model decline).
/// </summary>
public sealed class DeclineRecovery(
    EntityExtractionRunner runner,
    ExtractionPromptBuilder promptBuilder,
    EntityNameMatcher? matcher = null)
{
    private const string RecoveryDataSource = "decline-recovery";

    public async Task<EntityEnvelope?> TryRecoverAsync(
        DndMcpAICsharpFun.Domain.IngestionRecord record,
        EntityCandidate candidate,
        string sourceBook,
        string edition,
        Dictionary<EntityType, JsonElement> schemas,
        CancellationToken ct)
    {
        var rebound = candidate with { TypePrior = new[] { EntityType.Rule, EntityType.Lore } };
        var id = ExtractionEntityIds.RecordedEntityId(record, rebound, matcher, isOfficial: true);
        var prompt = promptBuilder.BuildRecoverySystemPrompt(sourceBook, edition);

        var (envelope, _) = await runner.ExtractOneAsync(
            record, rebound, id, sourceBook, edition, schemas, ct, isOfficial: true, systemPromptOverride: prompt);

        // Only an ACCEPTED envelope is a genuine recovery. The runner's grounding cascade runs with
        // the judge disabled during extraction, so an ungrounded pick's fields yield Uncertain, not
        // a drop — which ExtractionDispositionPolicy maps to NeedsReview, not Accepted. A non-Accepted
        // envelope (NeedsReview, Ungrounded, Declined, Failed) must NOT be admitted here: the candidate
        // stays declined, exactly as an ungrounded pick should (anti-fabrication contract).
        return envelope is not null && envelope.Disposition == EntityDisposition.Accepted
            ? envelope with { DataSource = RecoveryDataSource }
            : null;
    }
}