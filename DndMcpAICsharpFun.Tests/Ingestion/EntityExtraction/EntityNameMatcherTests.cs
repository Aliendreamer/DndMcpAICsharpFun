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
}
