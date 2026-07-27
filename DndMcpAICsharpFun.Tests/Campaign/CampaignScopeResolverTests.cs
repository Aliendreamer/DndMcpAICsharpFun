using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Campaign;

public sealed class CampaignScopeResolverTests
{
    private static DndMcpAICsharpFun.Domain.Campaign Make(DndVersion edition, params string[] books) =>
        new(1, 1, "T", "", DateTime.UtcNow, Setting: null, Edition: edition, AllowedBookKeys: books);

    // The motivating case: a 2024 table playing in a 2014-era setting. Strict edition scoping would
    // tell it "not in your edition" about its own setting, so lore must ignore edition.
    [Fact]
    public void A_2024_campaign_keeps_its_2014_setting_for_lore_but_not_for_mechanics()
    {
        var scope = CampaignScopeResolver.Resolve(Make(DndVersion.Edition2024, "XPHB", "XDMG", "ERLW"));

        scope.MechanicsScope.Should().BeEquivalentTo(["XPHB", "XDMG"],
            "ERLW is 2014-canon, so its subclasses and stat blocks are not this table's mechanics");
        scope.LoreScope.Should().BeEquivalentTo(["XPHB", "XDMG", "ERLW"],
            "Eberron's history is equally true under either canon");
    }

    [Fact]
    public void An_all_2014_campaign_has_identical_mechanics_and_lore_scopes()
    {
        var scope = CampaignScopeResolver.Resolve(Make(DndVersion.Edition2014, "PHB", "DMG", "ERLW", "XGE"));

        scope.MechanicsScope.Should().BeEquivalentTo(scope.LoreScope);
        scope.Edition.Should().Be(DndVersion.Edition2014);
    }

    [Fact]
    public void An_empty_allowlist_yields_empty_scopes_not_everything()
    {
        var scope = CampaignScopeResolver.Resolve(Make(DndVersion.Edition2014));

        scope.MechanicsScope.Should().BeEmpty();
        scope.LoreScope.Should().BeEmpty("empty means no books in scope — honest-empty, never 'all books'");
    }

    [Fact]
    public void An_unknown_key_is_dropped_rather_than_propagated()
    {
        var scope = CampaignScopeResolver.Resolve(Make(DndVersion.Edition2014, "PHB", "NOT_A_BOOK"));

        scope.LoreScope.Should().BeEquivalentTo(["PHB"]);
    }

    [Theory]
    [InlineData(DndVersion.Edition2014, "PHB", "DMG", "MM")]
    [InlineData(DndVersion.Edition2024, "XPHB", "XDMG", "XMM")]
    public void Core_default_is_the_editions_three_rulebooks(DndVersion edition, params string[] expected) =>
        CampaignScopeResolver.CoreBooksFor(edition).Should().BeEquivalentTo(expected);

    [Fact]
    public void Core_default_excludes_supplements()
    {
        // Guards the IsCore markers: a supplement accidentally marked core would silently widen every
        // new campaign's default scope.
        CampaignScopeResolver.CoreBooksFor(DndVersion.Edition2014)
            .Should().NotContain(["XGE", "TCE", "ERLW", "SCAG", "MTF", "MPMM"]);
    }
}