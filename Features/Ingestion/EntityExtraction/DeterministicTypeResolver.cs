using System.Collections.Frozen;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public enum DeterministicOutcome { Drop, ForceType, Defer, Decline }

public readonly record struct TypeResolution(DeterministicOutcome Outcome, EntityType ForcedType)
{
    public string? CanonicalName { get; init; }
    public string? DeclineReason { get; init; }
    public static readonly TypeResolution Drop = new(DeterministicOutcome.Drop, default);
    public static readonly TypeResolution Defer = new(DeterministicOutcome.Defer, default);
    public static TypeResolution Force(EntityType type, string? canonicalName = null) =>
        new(DeterministicOutcome.ForceType, type) { CanonicalName = canonicalName };
    public static TypeResolution Decline(string reason) =>
        new(DeterministicOutcome.Decline, default) { DeclineReason = reason };
}

/// <summary>
/// One deterministic per-candidate type decision, applied before the content-first union:
/// drop non-entity-named candidates → force Monster on a complete stat block (the name is already
/// entity-like, so the drop step is the override misfire guard) → force MagicItem on a magic-item
/// signature → for an official book, decline when the primary prior type is gated and unmatched;
/// otherwise defer to the content-first union.
/// </summary>
public static class DeterministicTypeResolver
{
    public static readonly IReadOnlySet<EntityType> GatedTypes = new HashSet<EntityType>
    {
        EntityType.Spell, EntityType.Monster, EntityType.Class, EntityType.Race,
        EntityType.Background, EntityType.Feat, EntityType.Condition, EntityType.God,
    }.ToFrozenSet();

    public static TypeResolution Resolve(EntityCandidate candidate, EntityNameMatcher? matcher = null, bool isOfficial = false)
    {
        var prior = candidate.TypePrior.Count > 0 ? candidate.TypePrior[0] : (EntityType?)null;
        var match = matcher?.Match(candidate.DisplayName);

        // Step 1 (highest priority): a 5etools match whose type equals the candidate's PRIMARY prior
        // wins outright — and runs BEFORE the drop filter so known entities are never discarded.
        // This keeps the common case (a real Spell "Fireball" with Spell prior → Spell) and resolves
        // a cross-type name collision in favour of the prior (Race "Dwarf" → the Dwarf Race entry,
        // not the Dwarf Monster entry).
        if (prior is { } p && matcher?.MatchOfType(candidate.DisplayName, p) is { } same)
            return TypeResolution.Force(same.Type, same.Canonical);

        // Drop non-entity-like names — but never drop a name that is a known 5etools entity.
        if (match is null && !ExtractionSignatures.IsEntityLikeName(candidate.DisplayName))
            return TypeResolution.Drop;

        // Stat-block monster rescue — MUST win over a cross-type name collision, so it runs before
        // the cross-type force below (a complete stat block resolves to Monster even if the name
        // matches a non-Monster 5etools entry).
        if (ExtractionSignatures.IsCompleteStatBlock(candidate.Text))
            return TypeResolution.Force(EntityType.Monster);
        if (ExtractionSignatures.IsMagicItem(candidate.Text))
            return TypeResolution.Force(EntityType.MagicItem);

        // A 5etools match exists but of a different type than the primary prior (no same-prior
        // match was found in Step 1) → force the best match regardless of prior type.
        // This is the correct behaviour for Dragonborn: Monster prior, only a Race match in 5etools
        // → Force(Race). The old gated-cross-type defer branch is intentionally absent here.
        if (match is { } m)
            return TypeResolution.Force(m.Type, m.Canonical);

        // Official-book gate: decline only when the primary prior is gated AND there was no
        // 5etools match at all (a cross-type match above is content, not grounds to decline).
        if (isOfficial
            && match is null
            && prior is { } pd
            && GatedTypes.Contains(pd))   // PRIMARY prior only (floor always adds Item)
            return TypeResolution.Decline("no_5etools_match");

        return TypeResolution.Defer;
    }
}
