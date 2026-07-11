using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.CharacterAdvice;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using DndMcpAICsharpFun.Tests.Persistence;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace DndMcpAICsharpFun.Tests.CharacterAdvice;

/// <summary>
/// Fake entity retrieval: for a Type=Class query, always returns a single canned Fighter
/// <c>ClassFields</c> entity (Hd + a couple classFeatures) regardless of query text — the
/// ownership/candidate/dip-validity logic under test does not depend on which real entity a
/// class name resolves to. All other queries (subclass/feat/spell option lookups) return empty,
/// since option-filling is not what these tests exercise.
/// </summary>
file sealed class FakeEntityRetrievalService : IEntityRetrievalService
{
    public Task<EntityFullResult?> GetByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult<EntityFullResult?>(null);

    public Task<IList<EntitySearchResult>> SearchAsync(EntitySearchQuery query, CancellationToken ct) =>
        Task.FromResult<IList<EntitySearchResult>>([]);

    public Task<IList<EntityDiagnosticResult>> SearchDiagnosticAsync(EntitySearchQuery query, CancellationToken ct)
    {
        if (query.Type != EntityType.Class)
            return Task.FromResult<IList<EntityDiagnosticResult>>([]);

        using var doc = JsonDocument.Parse("""
            {
              "hd": { "number": 1, "faces": 10 },
              "classFeatures": [
                "Fighting Style|Fighter|PHB|1",
                "Second Wind|Fighter|PHB|1"
              ],
              "subclassTitle": "Martial Archetype"
            }
            """);
        var result = new EntityDiagnosticResult(
            Id: "phb14.class.fighter",
            Type: EntityType.Class,
            Name: "Fighter",
            SourceBook: "PHB",
            Edition: "2014",
            Page: 72,
            SettingTags: [],
            PointId: "point-fighter",
            Fields: doc.RootElement.Clone(),
            Score: 1.0f);
        return Task.FromResult<IList<EntityDiagnosticResult>>([result]);
    }
}

/// <summary>
/// SEC-08-style coverage for <see cref="LevelUpAdviceService"/>: ownership gating (SHIP BLOCKER),
/// candidate construction for existing classes, and the illegal-dip contract — an ineligible
/// multiclass dip (failing <see cref="DndMcpAICsharpFun.Features.Resolution.MulticlassRules.CanMulticlassInto"/>)
/// is excluded from <see cref="LevelUpAdvice.Candidates"/> entirely rather than surfaced as a
/// recommendation. Entity retrieval is faked (a canned Fighter <c>ClassFields</c>); Qdrant is not
/// exercised here — see <c>EntityOptionProviderIntegrationTests</c> for that.
/// </summary>
[Collection("postgres")]
public sealed class LevelUpAdviceServiceTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private IDbContextFactory<AppDbContext> DbFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(opts =>
            opts.UseNpgsql(pg.Container.GetConnectionString()));
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    private LevelUpAdviceService BuildService(out HeroRepository heroes)
    {
        var dbf = DbFactory();
        heroes = new HeroRepository(dbf);
        var retrieval = new FakeEntityRetrievalService();
        return new LevelUpAdviceService(heroes, retrieval, new LevelUpPlanner(), new EntityOptionProvider(retrieval));
    }

    /// <summary>
    /// Seeds a full ownership chain (User → Campaign → Hero → HeroSnapshot) with the given sheet
    /// and returns the snapshot id together with the id of the user that owns it.
    /// </summary>
    private async Task<(long SnapshotId, long OwnerUserId)> SeedOwnedSnapshotAsync(CharacterSheet sheet)
    {
        await using var db = pg.NewContext();

        var user = new User(0, $"owner-{Guid.NewGuid():N}", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var campaign = new DndMcpAICsharpFun.Domain.Campaign(0, user.Id, "Owned Campaign", "", DateTime.UtcNow);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();

        var hero = new Hero(0, campaign.Id, "Test Hero", DateTime.UtcNow);
        db.Heroes.Add(hero);
        await db.SaveChangesAsync();

        var snapshot = new HeroSnapshot(0, hero.Id, 1, "L1", sheet.Level, DateTime.UtcNow, sheet);
        db.HeroSnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        return (snapshot.Id, user.Id);
    }

    private async Task<long> SeedUserAsync()
    {
        await using var db = pg.NewContext();
        var user = new User(0, $"other-{Guid.NewGuid():N}", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static CharacterSheet FighterSheet(int strength = 15) => new()
    {
        Classes = [new ClassLevel { Class = "Fighter", Subclass = "", Level = 3 }],
        Strength = strength,
        Constitution = 14,
        Dexterity = 8,
        Intelligence = 8,
        Wisdom = 8,
        Charisma = 8,
    };

    [Fact]
    public async Task PlanForUser_otherUsersSnapshot_throws()
    {
        var (snapshotId, _) = await SeedOwnedSnapshotAsync(FighterSheet());
        var otherUserId = await SeedUserAsync();
        var service = BuildService(out _);

        await FluentActions.Awaiting(() =>
                service.PlanForUserAsync(snapshotId, otherUserId, null, considerDip: false, default))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Hero snapshot not found or not owned by the caller.");
    }

    [Fact]
    public async Task PlanForUser_ownerGetsCandidateForEachClass()
    {
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(FighterSheet());
        var service = BuildService(out _);

        var advice = await service.PlanForUserAsync(snapshotId, ownerUserId, null, considerDip: false, default);

        advice.HeroSnapshotId.Should().Be(snapshotId);
        advice.Candidates.Should().Contain(c => c.ClassName == "Fighter" && !c.IsNewClassDip);
        advice.Candidates.Should().OnlyContain(c => c.DipValidity == null);
    }

    [Fact]
    public async Task ConsiderDip_illegalDip_isExcludedFromCandidates_notRecommended()
    {
        // Wizard requires Intelligence 13+ (MulticlassRules.Prereqs); Intelligence 8 here fails it,
        // so Wizard must never appear as a dip candidate — it is excluded, not silently invented
        // and not surfaced as an allowed recommendation.
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(FighterSheet());
        var service = BuildService(out _);

        var advice = await service.PlanForUserAsync(snapshotId, ownerUserId, null, considerDip: true, default);

        advice.Candidates.Should().NotContain(c => c.ClassName == "Wizard");

        // Non-vacuity: an eligible dip (Strength 15 satisfies Barbarian's prereq) IS offered,
        // proving the dip path runs and isn't just filtering everything out.
        advice.Candidates.Should().Contain(c => c.ClassName == "Barbarian" && c.IsNewClassDip
            && c.DipValidity != null && c.DipValidity.Allowed);
    }

    [Fact]
    public async Task ConsiderDip_targetClassSet_onlyThatClassDipIsCandidate()
    {
        // Str 15 makes Barbarian eligible; Cha 15 also makes Bard (and Sorcerer/Warlock) eligible.
        // With targetClass="Barbarian" set, only the Barbarian dip should surface — targetClass
        // scopes the dip search too, so it never balloons into a whole-roster suggestion list.
        var sheet = new CharacterSheet
        {
            Classes = [new ClassLevel { Class = "Fighter", Subclass = "", Level = 3 }],
            Strength = 15,
            Constitution = 14,
            Dexterity = 8,
            Intelligence = 8,
            Wisdom = 8,
            Charisma = 15,
        };
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(sheet);
        var service = BuildService(out _);

        var advice = await service.PlanForUserAsync(
            snapshotId, ownerUserId, targetClass: "Barbarian", considerDip: true, default);

        advice.Candidates.Should().Contain(c => c.ClassName == "Barbarian" && c.IsNewClassDip);
        advice.Candidates.Should().NotContain(c => c.ClassName == "Bard");
    }
}
