using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Npc;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using DndMcpAICsharpFun.Features.SessionPrep;
using DndMcpAICsharpFun.Tests.Persistence;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.SessionPrep;

[Collection("postgres")]
public sealed class SessionPrepServiceTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly HeroRepository _heroes = new(new TestDb(pg));
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // A monster source returning a fixed pool so the encounter build has candidates.
    private sealed class PoolMonsterSource(IReadOnlyList<MonsterRef> pool) : IEncounterMonsterSource
    {
        public Task<IReadOnlyList<MonsterRef>> FindAsync(
            DndVersion ed, double crGte, double crLte, string? theme, bool srdOnly, int limit, CancellationToken ct) =>
            Task.FromResult(pool);
    }

    private (SessionPrepService svc, IRagRetrievalService rag) Build()
    {
        IReadOnlyList<MonsterRef> pool = Enumerable.Range(1, 6)
            .Select(i => new MonsterRef($"mm.monster.{i}", $"Monster {i}", 3, EncounterMath.CrToXp(3))).ToList();
        var assessor = new EncounterAssessor();
        var encounters = new EncounterDesignService(
            assessor, new EncounterGenerator(new PoolMonsterSource(pool), assessor),
            _heroes, _campaigns, Substitute.For<IEntityRetrievalService>());

        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<RetrievalResult>()); // hooks empty is fine for these tests
        var lore = new SettingLoreService(_campaigns, rag);

        var npcs = new NpcGenerationService(Substitute.For<IEntityRetrievalService>()); // bogus archetype → not-in-corpus

        return (new SessionPrepService(encounters, npcs, lore), rag);
    }

    [Fact]
    public async Task Prep_composes_encounter_npc_and_hooks_for_an_owned_campaign()
    {
        var (svc, rag) = Build();
        var campaignId = await SeedCampaignWithHeroLevelAsync(userId: 1, level: 5); // reuse EncounterDesignServiceTests helper

        // Difficulty.Hard, not Medium: a lone L5 hero facing a CR-3 (700xp) monster pool gets a
        // x1.5 solo multiplier (adjusted 1050xp), landing squarely in the Hard band [750,1100) for
        // this level. Medium would make every same-CR candidate overshoot and the greedy build
        // would return zero monsters — a fixture mismatch, not a defect in the composition.
        var packet = await svc.PrepForUserAsync(
            1, campaignId, "Sharn intrigue", Difficulty.Hard, "Spy", DndVersion.Edition2014, CancellationToken.None);

        packet.Theme.Should().Be("Sharn intrigue");
        packet.Encounter.Should().NotBeNull();
        packet.Encounter.Assessment.Monsters.Should().NotBeEmpty();     // built from the pool
        packet.Npc.Archetype.Should().Be("Spy");                        // GeneratedNpc reflects the requested archetype
        packet.Npc.Concept.Should().Be("Sharn intrigue");               // and the theme passed through as its concept
        packet.LoreHooks.Should().NotBeNull();
        // lore question derived from the theme
        await rag.Received().SearchAsync(
            Arg.Is<RetrievalQuery>(q => q.QueryText.Contains("Sharn intrigue")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Foreign_campaign_throws_before_any_prep()
    {
        var (svc, _) = Build();
        var campaignId = await SeedCampaignWithHeroLevelAsync(userId: 2, level: 5); // owned by user 2

        var act = () => svc.PrepForUserAsync(
            1, campaignId, "x", Difficulty.Medium, "Spy", DndVersion.Edition2014, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // Copied verbatim from EncounterDesignServiceTests.
    private async Task<long> SeedCampaignWithHeroLevelAsync(long userId, int level)
    {
        var campaignId = await _campaigns.CreateAsync(userId, "Campaign", "desc");
        var heroId = await _heroes.CreateAsync(campaignId, "Hero");
        var sheet = new CharacterSheet();
        sheet.SetSingleClass("Fighter", "", level);
        await _heroes.SaveSnapshotAsync(heroId, 1, $"L{level}", sheet);
        return campaignId;
    }
}
