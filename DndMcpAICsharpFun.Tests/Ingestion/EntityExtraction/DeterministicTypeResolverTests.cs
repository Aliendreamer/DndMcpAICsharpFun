using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Tests;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class DeterministicTypeResolverTests
{
    // Shared across all test methods — builds the real 5etools index once.
    private static readonly EntityNameMatcher Matcher =
        new(new EntityNameIndex(TestPaths.RepoFile("5etools")));

    private static EntityCandidate C(string name, string text) =>
        new(EntityType.Monster, name, text, 1, new[] { EntityType.Monster });

    [Fact]
    public void Non_entity_name_is_dropped()
    {
        var r = DeterministicTypeResolver.Resolve(C("ACTIONS", "Armor Class 14 Hit Points 30 Challenge 1 (200 XP)"), Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.Drop);
    }

    [Fact]
    public void Complete_stat_block_with_creature_name_forces_Monster()
    {
        // "Aboleth" matches 5etools as Monster — step 1 wins, Outcome/ForcedType unchanged.
        var r = DeterministicTypeResolver.Resolve(C("Aboleth", "Large aberration  Armor Class 17  Hit Points 135  Challenge 10 (5,900 XP)"), Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Monster);
    }

    [Fact]
    public void Tutorial_fragment_stat_block_is_not_forced_Monster()
    {
        var r = DeterministicTypeResolver.Resolve(C("Step 2. Basic Statistics", "Armor Class 15  Hit Points 100  Challenge 5 (1,800 XP)"), Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.Drop);
    }

    [Fact]
    public void Magic_item_signature_forces_MagicItem()
    {
        var r = DeterministicTypeResolver.Resolve(C("Vorpal Sword", "Weapon (any sword that deals slashing damage), legendary (requires attunement)"), Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.MagicItem);
    }

    [Fact]
    public void Unmatched_entity_like_candidate_defers()
    {
        // Name is entity-like but not in 5etools; text has no stat-block or magic-item signature.
        var r = DeterministicTypeResolver.Resolve(C("Xyzmorphic Wisp", "A creature that dwells in the ethereal plane."), Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.Defer);
    }

    // ── Task 5: 5etools match as step 1 ──────────────────────────────────────────────

    [Fact]
    public void Fivetools_match_forces_type_and_canonical_name()
    {
        // FIREBALL → normalized "fireball" → exact 5etools hit → ForceType(Spell) + CanonicalName.
        var r = DeterministicTypeResolver.Resolve(
            C("FIREBALL", "A bright streak flashes to a point you choose, then blossoms into flame."), Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Spell);
        r.CanonicalName.Should().Be("Fireball");
    }

    [Fact]
    public void Fivetools_match_precedes_drop_filter_and_stat_block_detection()
    {
        // "FIREBALL" paired with stat-block text: without step 1 it would ForceType(Monster);
        // with step 1 the 5etools Spell match wins before either the drop filter or stat-block check.
        var r = DeterministicTypeResolver.Resolve(
            C("FIREBALL", "Armor Class 14 Hit Points 30 Challenge 1 (200 XP)"), Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Spell);
        r.CanonicalName.Should().Be("Fireball");
    }

    // ── Stat-block heuristic direct coverage ─────────────────────────────────────────

    [Fact]
    public void Non_5etools_creature_with_complete_stat_block_forces_Monster()
    {
        // "Xyzgoblin Elder" is purely fictional — the 5etools matcher returns null (step 1 is skipped).
        // The stat-block heuristic (IsCompleteStatBlock → Force(Monster)) then fires directly.
        var r = DeterministicTypeResolver.Resolve(
            C("Xyzgoblin Elder", "Armor Class 14 Hit Points 30 Challenge 1 (200 XP)"), Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Monster);
    }
}
