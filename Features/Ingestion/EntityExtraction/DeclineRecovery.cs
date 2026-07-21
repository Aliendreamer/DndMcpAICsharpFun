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

        // A non-null envelope means the runner's deterministic resolution + union call landed on
        // Rule or Lore AND the entity survived the grounding cascade + disposition — a genuine
        // recovery. A null result (none pick, extraction failure, or model decline) means the
        // candidate stays declined; the caller keeps it in the declined roster.
        return envelope is null ? null : envelope with { DataSource = RecoveryDataSource };
    }
}
