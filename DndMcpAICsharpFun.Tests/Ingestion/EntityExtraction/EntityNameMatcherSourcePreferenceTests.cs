using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

/// <summary>
/// Locks for extraction-roster-recovery-generalization: same-source preference with global fallback.
/// The motivating defect — EGW's race and subrace pages were emitted as Monster candidates because the
/// global first-wins index returns bestiary entries (loaded before races.json) for names like "Orc".
/// </summary>
public sealed class EntityNameMatcherSourcePreferenceTests
{
    private static readonly EntityNameMatcher Matcher =
        new(new EntityNameIndex(TestPaths.RepoFile("5etools")));

    [Fact]
    public void Prefers_the_books_own_source_over_another_books_entry()
    {
        // Global Match returns the bestiary Orc because bestiary-* loads before races.json.
        Matcher.Match("Orc")!.Value.Type.Should().Be(EntityType.Monster,
            "baseline: the global first-wins lookup is what mis-typed EGW's Orc page");

        Matcher.MatchPreferSource("Orc", "EGW")!.Value.Type.Should().Be(EntityType.Race,
            "EGW's own Orc entry is a Race, and that is what the book's page is about");
    }

    [Fact]
    public void Prefers_the_books_own_subrace_entry()
    {
        Matcher.MatchPreferSource("Pallid", "EGW")!.Value
            .Should().Be(("Pallid", EntityType.Subrace));
    }

    [Fact]
    public void Falls_back_to_global_when_the_book_has_no_matching_entry()
    {
        // "Aboleth" is not an EGW entity; the global bestiary entry must still be returned so a
        // reprint is recovered rather than dropped.
        Matcher.MatchPreferSource("Aboleth", "EGW")!.Value
            .Should().Be(("Aboleth", EntityType.Monster));
    }

    [Fact]
    public void Returns_null_when_nothing_matches_at_the_confidence_bar()
    {
        Matcher.MatchPreferSource("Zzyzxqqq Nonexistent Heading", "EGW").Should().BeNull();
    }

    [Fact]
    public void Existing_lookups_are_unchanged()
    {
        // Pins the documented behaviour of the untyped and type-restricted paths so the new method
        // cannot have altered them.
        Matcher.Match("FIREBALL")!.Value.Should().Be(("Fireball", EntityType.Spell));
        Matcher.Match("BARD")!.Value.Should().Be(("Bard", EntityType.Class));
        Matcher.MatchOfType("Orc", EntityType.Monster)!.Value.Type.Should().Be(EntityType.Monster);
        Matcher.MatchOfType("Fireball", EntityType.Monster).Should().BeNull();
    }

    [Fact]
    public void A_null_or_unknown_source_key_behaves_as_a_global_lookup()
    {
        Matcher.MatchPreferSource("Aboleth", null)!.Value.Type.Should().Be(EntityType.Monster);
        Matcher.MatchPreferSource("Aboleth", "NOT_A_SOURCE")!.Value.Type.Should().Be(EntityType.Monster);
    }
}