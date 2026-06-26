using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Collapses candidates that resolve to the same entity id (the section scanner and the
/// <see cref="StatBlockScanner"/> both emit a candidate for a header-clean monster) to a single
/// best input BEFORE extraction. Prefers a candidate whose text actually contains a stat block,
/// then the richer (longer) text — so a header-clean monster is extracted from its full-context
/// section text (which types reliably) rather than an isolated, context-poor stat block that the
/// model sometimes declines (the Aboleth/Bugbear regression), while a headerless monster keeps its
/// stat-block candidate (the only one carrying the block). First-occurrence order is preserved so
/// checkpoint/resume stays stable.
/// </summary>
public static class ExtractionCandidateDeduplicator
{
    public static List<EntityCandidate> Dedupe(IEnumerable<EntityCandidate> candidates, string bookDisplayName)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .GroupBy(c => EntityIdSlug.For(bookDisplayName, c.Type, c.DisplayName), StringComparer.Ordinal)
            .Select(g => g
                .OrderByDescending(c => ExtractionSignatures.HasArmorClass(c.Text) && ExtractionSignatures.HasHitPoints(c.Text))
                .ThenByDescending(c => c.Text.Length)
                .First())
            .ToList();
    }
}
