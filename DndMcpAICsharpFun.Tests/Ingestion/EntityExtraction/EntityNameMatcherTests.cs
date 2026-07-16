using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Tests;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class EntityNameMatcherTests
{
    // Shared across all test methods — builds the real index once.
    private static readonly EntityNameMatcher Matcher =
        new(new EntityNameIndex(TestPaths.RepoFile("5etools")));

    [Fact]
    public void Match_fireball_returns_spell() =>
        Matcher.Match("FIREBALL").Should().Be(("Fireball", EntityType.Spell));

    // "MAGEARMOR" → Normalize → "magearmor" == Normalize("Mage Armor") → EXACT hit.
    [Fact]
    public void Match_magearmor_merged_is_exact_hit() =>
        Matcher.Match("MAGEARMOR").Should().Be(("Mage Armor", EntityType.Spell));

    [Fact]
    public void Match_actions_heading_returns_null() =>
        Matcher.Match("ACTIONS").Should().BeNull();

    [Fact]
    public void Match_dragon_lair_section_heading_returns_null() =>
        Matcher.Match("A RED DRAGON'S LAIR").Should().BeNull();

    [Fact]
    public void Match_gibberish_below_threshold_returns_null() =>
        Matcher.Match("Zxqwv Nonsense").Should().BeNull();

    // True fuzzy case: "Thunderwve" has the 'a' dropped from "Thunderwave".
    // Normalized: "thunderwve" (10) vs "thunderwave" (11) → dist=1, maxLen=11, ratio≈0.909 ≥ 0.90.
    [Fact]
    public void Match_single_char_ocr_typo_fuzzy_hit() =>
        Matcher.Match("Thunderwve").Should().Be(("Thunderwave", EntityType.Spell));

    [Fact]
    public void Match_empty_string_returns_null() =>
        Matcher.Match("").Should().BeNull();

    [Fact]
    public void Match_whitespace_only_returns_null() =>
        Matcher.Match("   ").Should().BeNull();

    // ── MatchOfType: per-type lookup for cross-type collisions ───────────────────────

    // "Dwarf" exists as BOTH a Monster (bestiary) and a Race (races.json). The first-wins
    // Entries map keeps the Monster; MatchOfType can still recover the Race entry by type.
    [Fact]
    public void MatchOfType_dwarf_race_returns_the_race_entry() =>
        Matcher.MatchOfType("Dwarf", EntityType.Race).Should().Be(("Dwarf", EntityType.Race));

    [Fact]
    public void MatchOfType_dwarf_monster_returns_the_monster_entry() =>
        Matcher.MatchOfType("Dwarf", EntityType.Monster).Should().Be(("Dwarf", EntityType.Monster));

    // "Dwarf" is not a spell in the corpus → no entry of that type → null.
    [Fact]
    public void MatchOfType_dwarf_spell_returns_null() =>
        Matcher.MatchOfType("Dwarf", EntityType.Spell).Should().BeNull();

    // Default Match (first-wins) still returns the Monster for the colliding name.
    [Fact]
    public void Match_dwarf_first_wins_returns_monster() =>
        Matcher.Match("Dwarf").Should().Be(("Dwarf", EntityType.Monster));

    // Fuzzy, restricted to type: "Thunderwve" → Thunderwave Spell; no monster of that name.
    [Fact]
    public void MatchOfType_fuzzy_hit_of_type() =>
        Matcher.MatchOfType("Thunderwve", EntityType.Spell).Should().Be(("Thunderwave", EntityType.Spell));

    [Fact]
    public void MatchOfType_fuzzy_wrong_type_returns_null() =>
        Matcher.MatchOfType("Thunderwve", EntityType.Monster).Should().BeNull();

    [Fact]
    public void MatchOfType_empty_string_returns_null() =>
        Matcher.MatchOfType("", EntityType.Spell).Should().BeNull();


    // ── Monster stat-line stripping (mm-monster-name-and-precision #1) ──────────────

    // A garbled heading where OCR/Marker conversion merged the stat line onto the name; the
    // matcher must strip the trailing "<Size> <type>, <alignment>" suffix before normalizing so
    // it still resolves against the clean 5etools roster entry.
    [Fact]
    public void Match_ancient_black_dragon_statline_resolves_to_clean_name() =>
        Matcher.Match("ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil")
            .Should().Be(("Ancient Black Dragon", EntityType.Monster));

    [Fact]
    public void MatchOfType_animated_armor_statline_resolves_to_clean_name() =>
        Matcher.MatchOfType("ANIMATED ARMOR Medium construct, unaligned", EntityType.Monster)
            .Should().Be(("Animated Armor", EntityType.Monster));

    // ── Subclass roster (extraction-authority-ladder Tier 1) ─────────────────────────

    [Fact]
    public void Match_path_of_the_battlerager_returns_subclass() =>
        Matcher.Match("Path of the Battlerager").Should().Be(("Path of the Battlerager", EntityType.Subclass));

    // Bare shortName header (e.g. how a chapter subsection might be titled) still resolves.
    [Fact]
    public void Match_mastermind_shortname_returns_subclass() =>
        Matcher.Match("Mastermind").Should().Be(("Mastermind", EntityType.Subclass));

    // Base-class name wins over any subclass on a collision.
    [Fact]
    public void Match_barbarian_returns_class_not_subclass() =>
        Matcher.Match("Barbarian").Should().Be(("Barbarian", EntityType.Class));
}