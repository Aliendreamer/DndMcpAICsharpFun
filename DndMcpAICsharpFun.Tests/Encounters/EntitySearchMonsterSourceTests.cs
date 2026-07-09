using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Encounters;

public sealed class EntitySearchMonsterSourceTests
{
    private static EntityDiagnosticResult DiagnosticHit(string id, string name, string fieldsJson) =>
        new(id, EntityType.Monster, name, "MM", "Edition2014", null,
            Array.Empty<string>(), $"point-{id}", JsonDocument.Parse(fieldsJson).RootElement, 0.9f);

    [Fact]
    public async Task FindAsync_maps_a_monster_results_cr_to_the_right_xp()
    {
        var search = Substitute.For<IEntityRetrievalService>();
        var hit = DiagnosticHit("mm.monster.ogre", "Ogre", """{"cr":"3"}""");
        search.SearchDiagnosticAsync(Arg.Any<EntitySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityDiagnosticResult> { hit });

        var source = new EntitySearchMonsterSource(search);

        var result = await source.FindAsync(
            DndVersion.Edition2014, crGte: 0, crLte: 10, theme: null, srdOnly: false, limit: 10, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be("mm.monster.ogre");
        result[0].Cr.Should().Be(3);
        result[0].Xp.Should().Be(700); // EncounterMath.CrToXp(3) == 700
    }

    [Fact]
    public async Task FindAsync_maps_a_fractional_cr_string_to_the_right_xp()
    {
        var search = Substitute.For<IEntityRetrievalService>();
        var hit = DiagnosticHit("mm.monster.rat", "Giant Rat", """{"cr":"1/8"}""");
        search.SearchDiagnosticAsync(Arg.Any<EntitySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityDiagnosticResult> { hit });

        var source = new EntitySearchMonsterSource(search);

        var result = await source.FindAsync(
            DndVersion.Edition2014, crGte: 0, crLte: 1, theme: null, srdOnly: false, limit: 10, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Cr.Should().Be(0.125);
        result[0].Xp.Should().Be(25);
    }

    [Fact]
    public async Task FindAsync_skips_results_with_no_usable_cr()
    {
        var search = Substitute.For<IEntityRetrievalService>();
        var hit = DiagnosticHit("mm.monster.mystery", "Mystery", "{}"); // no "cr" property at all
        search.SearchDiagnosticAsync(Arg.Any<EntitySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityDiagnosticResult> { hit });

        var source = new EntitySearchMonsterSource(search);

        var result = await source.FindAsync(
            DndVersion.Edition2014, crGte: 0, crLte: 10, theme: null, srdOnly: false, limit: 10, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindAsync_forwards_the_monster_filters_into_the_query()
    {
        var search = Substitute.For<IEntityRetrievalService>();
        search.SearchDiagnosticAsync(Arg.Any<EntitySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityDiagnosticResult>());

        var source = new EntitySearchMonsterSource(search);

        await source.FindAsync(
            DndVersion.Edition2024, crGte: 1.5, crLte: 6, theme: "Undead", srdOnly: true, limit: 25, CancellationToken.None);

        await search.Received(1).SearchDiagnosticAsync(
            Arg.Is<EntitySearchQuery>(q =>
                q.Type == EntityType.Monster &&
                q.Edition == "Edition2024" &&
                q.Keyword == "Undead" &&
                q.CrNumericGte == 1.5 &&
                q.CrNumericLte == 6 &&
                q.Srd == true &&
                q.TopK == 25),
            Arg.Any<CancellationToken>());
    }
}
