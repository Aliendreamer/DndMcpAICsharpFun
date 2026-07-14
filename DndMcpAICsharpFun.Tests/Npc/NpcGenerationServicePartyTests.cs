using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Npc;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Npc;

public sealed class NpcGenerationServicePartyTests
{
    // A fake that grounds ANY queried archetype: the returned hit is named after the query text.
    private static IEntityRetrievalService EchoSearch(ISet<string>? missing = null)
    {
        var s = Substitute.For<IEntityRetrievalService>();
        s.SearchDiagnosticAsync(Arg.Any<EntitySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var name = ci.Arg<EntitySearchQuery>().QueryText;
                if (missing is not null && missing.Contains(name))
                    return new List<EntityDiagnosticResult>();
                var id = "mm.monster." + name.ToLowerInvariant().Replace(' ', '-');
                return new List<EntityDiagnosticResult> { new(id, EntityType.Monster, name, "MM",
                    "Edition2014", null, [], "pt",
                    JsonDocument.Parse("""{"cr":"1","hp":{"average":11},"dex":14}""").RootElement, 0.9f) };
            });
        s.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var id = ci.Arg<string>();
                var name = id.Replace("mm.monster.", "").Replace('-', ' ');
                return new EntityFullResult(new EntityEnvelope(id, EntityType.Monster, name, "MM",
                    "Edition2014", null, new FirstAppearance("MM","Edition2014"), [], [],
                    $"{name}\nAC 12\nHP 27\nSTR 10 DEX 15",
                    JsonDocument.Parse("""{"cr":"1","hp":{"average":27},"str":10,"dex":15,"con":10,"int":12,"wis":14,"cha":16}""").RootElement));
            });
        return s;
    }

    [Fact]
    public async Task Themed_party_grounds_every_member_in_roster_order()
    {
        var svc = new NpcGenerationService(EchoSearch());
        var party = await svc.GeneratePartyAsync("a Sharn heist", CancellationToken.None);

        party.Template.Should().Be("criminal");
        party.Members.Should().HaveCount(4);
        party.Members[0].Role.Should().Be("leader");
        party.Members[0].Npc.ArchetypeInCorpus.Should().BeTrue();
        party.Members[0].Npc.StatBlock!.Name.Should().Be("Bandit Captain");
        party.Members.Should().OnlyContain(m => m.Npc.ArchetypeInCorpus);
    }

    [Fact]
    public async Task Missing_archetype_is_flagged_but_party_still_returns()
    {
        var svc = new NpcGenerationService(EchoSearch(missing: new HashSet<string> { "Spy" }));
        var party = await svc.GeneratePartyAsync("criminal gang", CancellationToken.None);

        party.Members.Should().HaveCount(4);
        party.Members.Single(m => m.Role == "informant").Npc.ArchetypeInCorpus.Should().BeFalse();
        party.Members.Count(m => m.Npc.ArchetypeInCorpus).Should().Be(3);
    }
}
