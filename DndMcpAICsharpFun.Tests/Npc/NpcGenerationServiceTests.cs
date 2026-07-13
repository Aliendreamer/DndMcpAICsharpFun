using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Npc;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Npc;

public sealed class NpcGenerationServiceTests
{
    private static EntityDiagnosticResult Diag(string id, string name, string cr) =>
        new(id, EntityType.Monster, name, "MM", "Edition2014", null, [], "pt",
            JsonDocument.Parse($$"""{"cr":"{{cr}}","hp":{"average":11},"dex":14}""").RootElement, 0.9f);

    private static EntityFullResult Full(string id, string name) =>
        new(new EntityEnvelope(id, EntityType.Monster, name, "MM", "Edition2014", null,
            new FirstAppearance("MM", "Edition2014"), [], [],
            "Spy\nMedium humanoid\nAC 12\nHP 27\nSTR 10 DEX 15 ...",   // rendered block
            JsonDocument.Parse("""{"cr":"1","hp":{"average":27},"str":10,"dex":15,"con":10,"int":12,"wis":14,"cha":16}""").RootElement));

    private static IEntityRetrievalService Search(EntityDiagnosticResult? hit, EntityFullResult? full)
    {
        var s = Substitute.For<IEntityRetrievalService>();
        s.SearchDiagnosticAsync(Arg.Any<EntitySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(hit is null ? new List<EntityDiagnosticResult>() : new List<EntityDiagnosticResult> { hit });
        s.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(full);
        return s;
    }

    [Fact]
    public async Task Valid_archetype_returns_grounded_stat_block()
    {
        var svc = new NpcGenerationService(Search(Diag("mm.monster.spy", "Spy", "1"), Full("mm.monster.spy", "Spy")));

        var npc = await svc.GenerateAsync("a shifty dockworker", "Spy", maxCr: null, CancellationToken.None);

        npc.ArchetypeInCorpus.Should().BeTrue();
        npc.StatBlock!.Name.Should().Be("Spy");
        npc.StatBlock.SourceBook.Should().Be("MM");
        npc.StatBlock.CanonicalText.Should().Contain("AC 12");
        npc.StatBlock.Cr.Should().Be(1);
    }

    [Fact]
    public async Task Unknown_archetype_returns_not_in_corpus_with_roster_and_no_block()
    {
        var svc = new NpcGenerationService(Search(hit: null, full: null));

        var npc = await svc.GenerateAsync("x", "Nonexistent Archetype", null, CancellationToken.None);

        npc.ArchetypeInCorpus.Should().BeFalse();
        npc.StatBlock.Should().BeNull();
        npc.AvailableArchetypes.Should().BeEquivalentTo(NpcArchetypes.Common);
    }

    [Fact]
    public async Task Non_exact_name_hit_is_treated_as_not_in_corpus()
    {
        // search returns a DIFFERENT monster than the requested archetype — must NOT be accepted
        var svc = new NpcGenerationService(Search(Diag("mm.monster.spider", "Giant Spider", "1"), Full("mm.monster.spider", "Giant Spider")));

        var npc = await svc.GenerateAsync("x", "Spy", null, CancellationToken.None);

        npc.ArchetypeInCorpus.Should().BeFalse();
        npc.StatBlock.Should().BeNull();
    }

    [Fact]
    public async Task Archetype_over_maxCr_is_rejected_with_roster()
    {
        var svc = new NpcGenerationService(Search(Diag("mm.monster.mage", "Mage", "6"), Full("mm.monster.mage", "Mage")));

        var npc = await svc.GenerateAsync("a wizard", "Mage", maxCr: 2, CancellationToken.None);

        npc.ArchetypeInCorpus.Should().BeFalse();
        npc.AvailableArchetypes.Should().NotBeEmpty();
    }
}
