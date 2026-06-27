using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public enum DeterministicOutcome { Drop, ForceType, Defer }

public readonly record struct TypeResolution(DeterministicOutcome Outcome, EntityType ForcedType)
{
    public string? CanonicalName { get; init; }
    public static readonly TypeResolution Drop = new(DeterministicOutcome.Drop, default);
    public static readonly TypeResolution Defer = new(DeterministicOutcome.Defer, default);
    public static TypeResolution Force(EntityType type, string? canonicalName = null) =>
        new(DeterministicOutcome.ForceType, type) { CanonicalName = canonicalName };
}

/// <summary>
/// One deterministic per-candidate type decision, applied before the content-first union:
/// drop non-entity-named candidates → force Monster on a complete stat block (the name is already
/// entity-like, so the drop step is the override misfire guard) → force MagicItem on a magic-item
/// signature → otherwise defer to the union pick-or-decline.
/// </summary>
public static class DeterministicTypeResolver
{
    public static TypeResolution Resolve(EntityCandidate candidate, EntityNameMatcher? matcher = null)
    {
        // Step 1 (highest priority): 5etools name match — force type and carry canonical name.
        // Runs BEFORE the drop filter so known entities are never silently discarded.
        if (matcher?.Match(candidate.DisplayName) is { } m)
            return TypeResolution.Force(m.Type, m.Canonical);

        if (!ExtractionSignatures.IsEntityLikeName(candidate.DisplayName))
            return TypeResolution.Drop;
        if (ExtractionSignatures.IsCompleteStatBlock(candidate.Text))
            return TypeResolution.Force(EntityType.Monster);
        if (ExtractionSignatures.IsMagicItem(candidate.Text))
            return TypeResolution.Force(EntityType.MagicItem);
        return TypeResolution.Defer;
    }
}
