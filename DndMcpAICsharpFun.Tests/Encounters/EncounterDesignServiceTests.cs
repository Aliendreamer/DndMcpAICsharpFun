using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using DndMcpAICsharpFun.Tests.Persistence;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Encounters;

/// <summary>
/// Real Postgres (Testcontainers) for <see cref="CampaignRepository"/>/<see cref="HeroRepository"/>
/// — both are sealed, concrete classes over <c>IDbContextFactory&lt;AppDbContext&gt;</c>, so the
/// lightest genuine test of the ownership check is the same real-repo pattern the persistence
/// tests already use (<see cref="PostgresFixture"/> + <see cref="TestDb"/>), not a hand-rolled fake.
/// A fake <see cref="IEncounterMonsterSource"/> captures the CR band the generator derives from the
/// resolved party's average level, which is how these tests observe party resolution indirectly
/// without needing to expose the private party-resolution result directly.
/// </summary>
[Collection("postgres")]
public sealed class EncounterDesignServiceTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly HeroRepository _heroes = new(new TestDb(pg));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class CapturingMonsterSource : IEncounterMonsterSource
    {
        public double? CapturedCrGte { get; private set; }
        public double? CapturedCrLte { get; private set; }

        public Task<IReadOnlyList<MonsterRef>> FindAsync(
            DndVersion ed, double crGte, double crLte, string? theme, bool srdOnly, int limit, CancellationToken ct)
        {
            CapturedCrGte = crGte;
            CapturedCrLte = crLte;
            return Task.FromResult<IReadOnlyList<MonsterRef>>(Array.Empty<MonsterRef>());
        }
    }

    private EncounterDesignService BuildService(
        IEncounterMonsterSource? source = null, IEntityRetrievalService? search = null)
    {
        var assessor = new EncounterAssessor();
        var generator = new EncounterGenerator(source ?? new CapturingMonsterSource(), assessor);
        return new EncounterDesignService(
            assessor, generator, _heroes, _campaigns, search ?? Substitute.For<IEntityRetrievalService>());
    }

    private async Task<long> SeedCampaignWithHeroLevelAsync(long userId, int level)
    {
        var campaignId = await _campaigns.CreateAsync(userId, "Campaign", "desc");
        var heroId = await _heroes.CreateAsync(campaignId, "Hero");
        var sheet = new CharacterSheet();
        sheet.SetSingleClass("Fighter", "", level);
        await _heroes.SaveSnapshotAsync(heroId, 1, $"L{level}", sheet);
        return campaignId;
    }

    [Fact]
    public async Task BuildForUserAsync_resolves_the_party_from_the_owned_campaigns_hero_levels()
    {
        var source = new CapturingMonsterSource();
        var service = BuildService(source: source);
        var campaignId = await SeedCampaignWithHeroLevelAsync(userId: 1, level: 8);

        await service.BuildForUserAsync(
            1, campaignId, partyLevels: null, Difficulty.Trivial, DndVersion.Edition2014, theme: null, CancellationToken.None);

        // The generator derives its default CR band from partyLevels.Average(); a single L8 hero
        // proves the party used was in fact this campaign's hero (crLte = max(1, 8) = 8,
        // crGte = max(0, 8/4) = 2), since no other value could produce this band.
        source.CapturedCrLte.Should().Be(8.0);
        source.CapturedCrGte.Should().Be(2.0);
    }

    [Fact]
    public async Task PartyLevels_override_wins_over_the_campaigns_heroes()
    {
        var source = new CapturingMonsterSource();
        var service = BuildService(source: source);
        var campaignId = await SeedCampaignWithHeroLevelAsync(userId: 1, level: 8);

        await service.BuildForUserAsync(
            1, campaignId, partyLevels: [2], Difficulty.Trivial, DndVersion.Edition2014, theme: null, CancellationToken.None);

        source.CapturedCrLte.Should().Be(2.0); // from the explicit partyLevels, not the L8 hero
        source.CapturedCrGte.Should().Be(0.5);
    }

    [Fact]
    public async Task BuildForUserAsync_throws_when_the_campaign_is_owned_by_a_different_user()
    {
        var source = new CapturingMonsterSource();
        var service = BuildService(source: source);
        var campaignId = await SeedCampaignWithHeroLevelAsync(userId: 2, level: 20); // decoy owner

        var act = () => service.BuildForUserAsync(
            1, campaignId, partyLevels: null, Difficulty.Trivial, DndVersion.Edition2014, theme: null, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        // The decoy owner's L20 hero was never fetched/used: if it had been, the captured CR band
        // would reflect level 20 instead of staying unset.
        source.CapturedCrLte.Should().BeNull();
    }

    [Fact]
    public async Task BuildForUserAsync_throws_when_the_campaign_does_not_exist()
    {
        var service = BuildService();

        var act = () => service.BuildForUserAsync(
            1, campaignId: 999_999, partyLevels: null, Difficulty.Trivial, DndVersion.Edition2014, theme: null, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RateForUserAsync_throws_when_the_campaign_is_owned_by_a_different_user()
    {
        var service = BuildService();
        var campaignId = await SeedCampaignWithHeroLevelAsync(userId: 2, level: 20);

        var act = () => service.RateForUserAsync(
            1, campaignId, partyLevels: null, monsters: [], DndVersion.Edition2014, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RateForUserAsync_throws_when_the_campaign_does_not_exist()
    {
        var service = BuildService();

        var act = () => service.RateForUserAsync(
            1, campaignId: 999_999, partyLevels: null, monsters: [], DndVersion.Edition2014, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Neither_campaignId_nor_partyLevels_throws_ArgumentException()
    {
        var service = BuildService();

        var rateAct = () => service.RateForUserAsync(
            1, campaignId: null, partyLevels: null, monsters: [], DndVersion.Edition2014, CancellationToken.None);
        var buildAct = () => service.BuildForUserAsync(
            1, campaignId: null, partyLevels: null, Difficulty.Trivial, DndVersion.Edition2014, theme: null, CancellationToken.None);

        await rateAct.Should().ThrowAsync<ArgumentException>();
        await buildAct.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Empty_partyLevels_falls_back_to_campaignId_rather_than_treating_it_as_an_override()
    {
        var source = new CapturingMonsterSource();
        var service = BuildService(source: source);
        var campaignId = await SeedCampaignWithHeroLevelAsync(userId: 1, level: 8);

        await service.BuildForUserAsync(
            1, campaignId, partyLevels: [], Difficulty.Trivial, DndVersion.Edition2014, theme: null, CancellationToken.None);

        source.CapturedCrLte.Should().Be(8.0); // empty list is treated as "not supplied", not [] party
    }


    private static EntityFullResult FullResultWithCr(string id, string name, string crJson) =>
        new(new EntityEnvelope(
            Id: id,
            Type: EntityType.Monster,
            Name: name,
            SourceBook: "MM",
            Edition: "Edition2014",
            Page: null,
            FirstAppearedIn: new FirstAppearance("MM", "Edition2014"),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: JsonDocument.Parse($$"""{"cr":"{{crJson}}"}""").RootElement));

    [Fact]
    public async Task RateForUserAsync_resolves_a_monster_by_id_and_reflects_its_xp()
    {
        var search = Substitute.For<IEntityRetrievalService>();
        search.GetByIdAsync("mm.monster.ogre", Arg.Any<CancellationToken>())
            .Returns(FullResultWithCr("mm.monster.ogre", "Ogre", "3"));
        var service = BuildService(search: search);

        var assessment = await service.RateForUserAsync(
            1, campaignId: null, partyLevels: [5], monsters: ["mm.monster.ogre"], DndVersion.Edition2014, CancellationToken.None);

        assessment.Monsters.Should().ContainSingle();
        assessment.Monsters[0].Xp.Should().Be(700); // EncounterMath.CrToXp(3) == 700
    }

    [Fact]
    public async Task RateForUserAsync_throws_ArgumentException_when_the_monster_is_unresolvable()
    {
        var search = Substitute.For<IEntityRetrievalService>();
        search.GetByIdAsync("nonexistent", Arg.Any<CancellationToken>()).Returns((EntityFullResult?)null);
        search.SearchDiagnosticAsync(Arg.Any<EntitySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityDiagnosticResult>());
        var service = BuildService(search: search);

        var act = () => service.RateForUserAsync(
            1, campaignId: null, partyLevels: [5], monsters: ["nonexistent"], DndVersion.Edition2014, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RateForUserAsync_throws_ArgumentException_not_ArgumentOutOfRangeException_for_an_off_table_cr()
    {
        // CR 3.5 parses fine via MonsterCr.TryRead/double.TryParse but is not one of the discrete
        // CrToXp table entries (0, 1/8, 1/4, 1/2, 1..30) — EncounterMath.CrToXp throws
        // ArgumentOutOfRangeException for it, which ResolveMonsterAsync must translate into the
        // same "not found or has no usable CR" ArgumentException used for the unresolvable case.
        var search = Substitute.For<IEntityRetrievalService>();
        search.GetByIdAsync("mm.monster.off-table", Arg.Any<CancellationToken>())
            .Returns(FullResultWithCr("mm.monster.off-table", "Off-Table", "3.5"));
        var service = BuildService(search: search);

        var act = () => service.RateForUserAsync(
            1, campaignId: null, partyLevels: [5], monsters: ["mm.monster.off-table"], DndVersion.Edition2014, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Should().NotBeOfType<ArgumentOutOfRangeException>();
    }
}