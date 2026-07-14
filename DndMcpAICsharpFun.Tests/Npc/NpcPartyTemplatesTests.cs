using DndMcpAICsharpFun.Features.Npc;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Npc;

public sealed class NpcPartyTemplatesTests
{
    [Fact]
    public void Criminal_keyword_selects_bandit_captain_led_roster()
    {
        var (name, roster) = NpcPartyTemplates.Resolve("a Sharn heist crew");
        name.Should().Be("criminal");
        roster[0].Archetype.Should().Be("Bandit Captain");
        roster.Select(r => r.Archetype).Should().Contain("Thug").And.Contain("Spy");
    }

    [Fact]
    public void Unmatched_theme_falls_back_to_default_never_empty()
    {
        var (name, roster) = NpcPartyTemplates.Resolve("a quiet afternoon in the meadow");
        name.Should().Be("default");
        roster.Should().NotBeEmpty();
        roster[0].Archetype.Should().Be("Veteran");
    }

    [Fact]
    public void Every_roster_archetype_is_a_grounded_common_member()
    {
        foreach (var t in NpcPartyTemplates.All)
            foreach (var (_, archetype) in t.Roster)
                NpcArchetypes.Common.Should().Contain(archetype, $"template '{t.Name}' references {archetype}");
        foreach (var (_, archetype) in NpcPartyTemplates.DefaultRoster)
            NpcArchetypes.Common.Should().Contain(archetype);
    }
}
