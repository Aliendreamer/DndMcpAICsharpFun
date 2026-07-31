using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Retrieval;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Campaign;

/// <summary>
/// The campaign forms' scope summary. Both the create form and the detail page render these strings,
/// so the formatting rules are tested once here rather than eyeballed twice in the browser.
/// </summary>
public sealed class CampaignScopeDisplayTests
{
    private static CampaignScope Scope(DndVersion edition, params string[] allowed) =>
        CampaignScopeResolver.Resolve(
            new DndMcpAICsharpFun.Domain.Campaign(1, 1, "C", "d", DateTime.UtcNow, null, edition, allowed));

    [Fact]
    public void Mechanics_names_every_book_when_the_scope_is_small()
    {
        var text = CampaignScopeDisplay.Mechanics(Scope(DndVersion.Edition2014, "PHB", "MM", "DMG"));

        text.Split(", ").Should().BeEquivalentTo(
            "PlayerHandbook 2014", "Monster Manual 2014", "Dungeon Master's Guide 2014");
        text.Should().NotContain("more", "nothing is collapsed at or below the cap");
    }

    // The migrated-campaign case: all sixteen 2014 books. Naming every one wrapped the detail page's
    // summary to three dense lines, so the tail collapses to a count.
    [Fact]
    public void Mechanics_caps_the_names_and_counts_the_rest_when_the_scope_is_large()
    {
        var all2014 = BookCatalog.All
            .Where(b => b.Version == DndVersion.Edition2014)
            .Select(b => b.Key)
            .ToArray();
        all2014.Length.Should().BeGreaterThan(3, "the cap is only exercised when the scope exceeds it");

        var text = CampaignScopeDisplay.Mechanics(Scope(DndVersion.Edition2014, all2014));

        // Exactly three books named (two separators), then the count — asserted without depending on
        // which three, since MechanicsScope is an unordered set.
        var named = text[..text.IndexOf(" +", StringComparison.Ordinal)];
        named.Split(", ").Should().HaveCount(3);
        text.Should().EndWith($"+{all2014.Length - 3} more");
        // The whole point of the cap: the long tail is NOT named.
        text.Should().NotContain("Elemental Evil");
    }

    // Honest-empty is a real state, not a missing value — the caller pairs this with a warning.
    [Fact]
    public void Mechanics_renders_an_empty_scope_as_none()
    {
        CampaignScopeDisplay.Mechanics(Scope(DndVersion.Edition2024, "PHB", "MM")).Should().Be("none");
    }

    [Theory]
    [InlineData(new[] { "PHB" }, "1 book")]
    [InlineData(new[] { "PHB", "ERLW" }, "2 books")]
    public void Lore_counts_every_allowed_book_regardless_of_edition(string[] allowed, string expected)
    {
        // Edition2024 with 2014 books allowed: lore is edition-portable, so all of them count.
        CampaignScopeDisplay.Lore(Scope(DndVersion.Edition2024, allowed)).Should().Be(expected);
    }

    [Fact]
    public void Lore_renders_an_empty_scope_as_zero_books()
    {
        CampaignScopeDisplay.Lore(Scope(DndVersion.Edition2014)).Should().Be("0 books");
    }
}