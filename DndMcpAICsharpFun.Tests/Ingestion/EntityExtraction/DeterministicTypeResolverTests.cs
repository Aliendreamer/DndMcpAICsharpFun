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
        // FIREBALL with a Spell prior → same-prior 5etools hit → ForceType(Spell) + CanonicalName.
        var r = DeterministicTypeResolver.Resolve(
            Candidate("FIREBALL", "A bright streak flashes to a point you choose, then blossoms into flame.", EntityType.Spell),
            Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Spell);
        r.CanonicalName.Should().Be("Fireball");
    }

    [Fact]
    public void Fivetools_match_precedes_drop_filter_and_stat_block_detection()
    {
        // "FIREBALL" (Spell prior) paired with stat-block text: the same-prior 5etools Spell match
        // wins before either the drop filter or the stat-block check.
        var r = DeterministicTypeResolver.Resolve(
            Candidate("FIREBALL", "Armor Class 14 Hit Points 30 Challenge 1 (200 XP)", EntityType.Spell),
            Matcher);
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

    // ── Task 1: Decline outcome + gated-type set ──────────────────────────────────────

    [Fact]
    public void GatedTypes_contains_the_eight_and_excludes_item_magicitem_plane()
    {
        DeterministicTypeResolver.GatedTypes.Should().BeEquivalentTo(new[]
        {
            EntityType.Spell, EntityType.Monster, EntityType.Class, EntityType.Race,
            EntityType.Background, EntityType.Feat, EntityType.Condition, EntityType.God
        });
        DeterministicTypeResolver.GatedTypes.Should().NotContain(EntityType.Item);
        DeterministicTypeResolver.GatedTypes.Should().NotContain(EntityType.MagicItem);
        DeterministicTypeResolver.GatedTypes.Should().NotContain(EntityType.Plane);
    }

    [Fact]
    public void Decline_carries_outcome_and_reason()
    {
        var r = TypeResolution.Decline("no_5etools_match");
        r.Outcome.Should().Be(DeterministicOutcome.Decline);
        r.DeclineReason.Should().Be("no_5etools_match");
    }

    // ── Task 2: isOfficial gate ───────────────────────────────────────────────────────

    // Candidate factory for gate tests: accepts a single prior type.
    private static EntityCandidate Candidate(string name, string text, EntityType prior) =>
        new(prior, name, text, null, new[] { prior });

    // Candidate factory for gate tests: accepts an array of prior types.
    private static EntityCandidate Candidate(string name, string text, IReadOnlyList<EntityType> prior) =>
        new(prior.Count > 0 ? prior[0] : EntityType.Monster, name, text, null, prior);

    [Fact] // official + all-gated prior + no match + no stat block -> Decline
    public void Official_gated_nonmatch_declines()
    {
        var c = Candidate("Rage", text: "", prior: EntityType.Class);
        var r = DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: true);
        r.Outcome.Should().Be(DeterministicOutcome.Decline);
        r.DeclineReason.Should().Be("no_5etools_match");
    }

    [Fact] // homebrew -> Defer (gate never fires)
    public void Homebrew_gated_nonmatch_defers()
    {
        var c = Candidate("Rage", text: "", prior: EntityType.Class);
        DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: false)
            .Outcome.Should().Be(DeterministicOutcome.Defer);
    }

    [Fact] // official + ungated primary (Item first) -> Defer (gate only checks TypePrior[0])
    public void Official_ungated_primary_defers()
    {
        var c = Candidate("Some Thing", text: "", prior: new[] { EntityType.Item, EntityType.Class });
        DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: true)
            .Outcome.Should().Be(DeterministicOutcome.Defer);
    }

    [Fact] // official + gated primary (Class) + ungated Item in floor -> Decline (regression guard)
    // This proves the gate keys off TypePrior[0] only, not .All(). The scanner floor always
    // appends {Monster, Spell, Item, Class} so a pure .All() check would never fire for Item.
    public void Official_gated_primary_with_floor_declines()
    {
        var c = Candidate("Some Thing", text: "", prior: new[] { EntityType.Class, EntityType.Item });
        DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: true)
            .Outcome.Should().Be(DeterministicOutcome.Decline);
    }

    [Fact] // official + empty prior -> Defer
    public void Official_empty_prior_defers()
    {
        var c = Candidate("Some Thing", text: "", prior: Array.Empty<EntityType>());
        DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: true)
            .Outcome.Should().Be(DeterministicOutcome.Defer);
    }

    [Fact] // official + stat block + no match -> Force Monster (guard wins, NOT Decline)
    public void Official_statblock_nonmatch_forces_monster_not_decline()
    {
        var c = Candidate("Xyzgoblin Elder", text: "Armor Class 15\nHit Points 40\nChallenge 3", prior: EntityType.Monster);
        var r = DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: true);
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Monster);
    }

    [Fact] // non-entity-like name -> Drop (before decline), even official+gated
    public void Official_nonentitylike_drops_not_declines()
    {
        var c = Candidate("ACTIONS", text: "", prior: EntityType.Monster);
        DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: true)
            .Outcome.Should().Be(DeterministicOutcome.Drop);
    }

    // ── Cross-type name collision prefers the candidate's PRIMARY prior type ───────────

    [Fact] // "Dwarf" matches both a Monster and a Race in 5etools; a Race prior wins the Race entry.
    public void Cross_type_collision_prefers_prior_Race_for_Dwarf()
    {
        var r = DeterministicTypeResolver.Resolve(
            Candidate("Dwarf", "A short and stout folk.", EntityType.Race), Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Race);
        r.CanonicalName.Should().Be("Dwarf");
    }

    [Fact] // Dragonborn: Monster prior, no Monster "Dragonborn" in index → only Race match → Force(Race).
    public void Dragonborn_gated_monster_prior_only_race_match_forces_race()
    {
        // Real regression: "Dragonborn" was mis-typed as Monster when the gated-defer branch
        // deferred to content-first for gated-prior + cross-type match. The fix forces
        // the cross-type Race match instead of deferring. In the real 5etools index, the
        // nearest Monster to "Dragonborn" is e.g. "Dragonborn (Red)" — >2 chars off — so
        // MatchOfType("Dragonborn", Monster) = null, while Match("Dragonborn") = Race "Dragonborn"
        // (exact hit in races.json). The resolver must Force(Race), not Defer.
        var r = DeterministicTypeResolver.Resolve(
            Candidate("Dragonborn", "A proud and draconic folk.", EntityType.Monster), Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Race);
        r.CanonicalName.Should().Be("Dragonborn");
    }

    [Fact] // Cross-type match, prior NOT gated (Item) → unchanged behaviour: force the single match.
    public void Cross_type_match_nongated_prior_forces_match()
    {
        var r = DeterministicTypeResolver.Resolve(
            Candidate("Dwarf", "Some thing.", EntityType.Item), Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Monster);   // first-wins best match for "Dwarf"
    }

    [Fact] // Stat-block rescue MUST win over a cross-type name match.
    public void Stat_block_rescue_wins_over_cross_type_name_match()
    {
        // "Fireball" matches a (non-Monster) Spell; with a complete stat block and a Class prior,
        // the monster rescue still wins — Monster, not the cross-type Spell match.
        var r = DeterministicTypeResolver.Resolve(
            Candidate("Fireball", "Armor Class 14 Hit Points 30 Challenge 1 (200 XP)", EntityType.Class),
            Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Monster);
    }

    [Fact] // Common case: a real Spell named "Fireball" with a Spell prior → Force Spell (unchanged).
    public void Same_type_match_forces_unchanged()
    {
        var r = DeterministicTypeResolver.Resolve(
            Candidate("Fireball", "A bright streak flashes into flame.", EntityType.Spell), Matcher);
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Spell);
        r.CanonicalName.Should().Be("Fireball");
    }
}
