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

        // Echoes the queried class name back as the entity's Name, so every queried class is
        // "in the corpus" under the stricter exact-name-match grounding rule.
        var className = query.QueryText;
        var slug = className.ToLowerInvariant();
        using var doc = JsonDocument.Parse($$"""
            {
              "hd": { "number": 1, "faces": 10 },
              "classFeatures": [
                "Fighting Style|{{className}}|PHB|1",
                "Second Wind|{{className}}|PHB|1"
              ],
              "subclassTitle": "Martial Archetype"
            }
            """);
        var result = new EntityDiagnosticResult(
            Id: $"phb14.class.{slug}",
            Type: EntityType.Class,
            Name: className,
            SourceBook: "PHB",
            Edition: "Edition2014",
            Page: 72,
            SettingTags: [],
            PointId: $"point-{slug}",
            Fields: doc.RootElement.Clone(),
            Score: 1.0f);
        return Task.FromResult<IList<EntityDiagnosticResult>>([result]);
    }
}

/// <summary>
/// Always returns a "Warlock" class entity regardless of the query — simulates the corpus
/// containing some class but never the one actually requested (e.g. Barbarian/Fighter absent).
/// Used to prove the exact-name-match-or-skip fix: the wrong-named candidate must never be
/// silently used to ground advice.
/// </summary>
file sealed class FakeWrongClassEntityRetrievalService : IEntityRetrievalService
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
              "hd": { "number": 1, "faces": 8 },
              "classFeatures": [
                "Otherworldly Patron|Warlock|PHB|1",
                "Pact Magic|Warlock|PHB|1"
              ],
              "subclassTitle": "Otherworldly Patron"
            }
            """);
        var result = new EntityDiagnosticResult(
            Id: "phb14.class.warlock",
            Type: EntityType.Class,
            Name: "Warlock",
            SourceBook: "PHB",
            Edition: "2014",
            Page: 106,
            SettingTags: [],
            PointId: "point-warlock",
            Fields: doc.RootElement.Clone(),
            Score: 1.0f);
        return Task.FromResult<IList<EntityDiagnosticResult>>([result]);
    }
}


/// <summary>
/// Returns TWO same-named class entities for a Type=Class query: a wrong-edition Edition2024
/// entity (hd d8) FIRST, and the correct Edition2014 entity (hd d10, matching the real PHB
/// Fighter) second. Proves the edition pin in <see cref="LevelUpAdviceService"/> (the
/// <c>LevelUpEdition = "Edition2014"</c> constant): the slot tables (<see cref="DndMcpAICsharpFun.Features.Resolution.MulticlassSlotTableSeeder"/>)
/// and <see cref="DndMcpAICsharpFun.Features.Resolution.MulticlassRules"/> are PHB-2014-only, so a
/// naive first-name-match (ignoring edition) would silently ground HP math on the wrong (2024)
/// hit die. Putting the wrong edition first makes this test fail without the fix — a
/// non-vacuous proof.
/// </summary>
file sealed class FakeMultiEditionClassRetrievalService : IEntityRetrievalService
{
    public Task<EntityFullResult?> GetByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult<EntityFullResult?>(null);

    public Task<IList<EntitySearchResult>> SearchAsync(EntitySearchQuery query, CancellationToken ct) =>
        Task.FromResult<IList<EntitySearchResult>>([]);

    public Task<IList<EntityDiagnosticResult>> SearchDiagnosticAsync(EntitySearchQuery query, CancellationToken ct)
    {
        if (query.Type != EntityType.Class)
            return Task.FromResult<IList<EntityDiagnosticResult>>([]);

        var className = query.QueryText;
        var slug = className.ToLowerInvariant();

        using var doc2024 = JsonDocument.Parse("""
            {
              "hd": { "number": 1, "faces": 8 },
              "classFeatures": [],
              "subclassTitle": "Martial Archetype"
            }
            """);
        using var doc2014 = JsonDocument.Parse($$"""
            {
              "hd": { "number": 1, "faces": 10 },
              "classFeatures": [
                "Fighting Style|{{className}}|PHB|1",
                "Second Wind|{{className}}|PHB|1"
              ],
              "subclassTitle": "Martial Archetype"
            }
            """);

        var wrongEdition = new EntityDiagnosticResult(
            Id: $"phb24.class.{slug}",
            Type: EntityType.Class,
            Name: className,
            SourceBook: "PHB",
            Edition: "Edition2024",
            Page: 72,
            SettingTags: [],
            PointId: $"point-2024-{slug}",
            Fields: doc2024.RootElement.Clone(),
            Score: 1.0f);

        var correctEdition = new EntityDiagnosticResult(
            Id: $"phb14.class.{slug}",
            Type: EntityType.Class,
            Name: className,
            SourceBook: "PHB",
            Edition: "Edition2014",
            Page: 72,
            SettingTags: [],
            PointId: $"point-2014-{slug}",
            Fields: doc2014.RootElement.Clone(),
            Score: 1.0f);

        // Wrong edition listed FIRST: a service that just took FirstOrDefault(name match) without
        // filtering on edition would ground on the 2024 (d8) entity instead of the 2014 (d10) one.
        return Task.FromResult<IList<EntityDiagnosticResult>>([wrongEdition, correctEdition]);
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

    private LevelUpAdviceService BuildService(out HeroRepository heroes, IEntityRetrievalService? retrieval = null)
    {
        var dbf = DbFactory();
        heroes = new HeroRepository(dbf);
        retrieval ??= new FakeEntityRetrievalService();
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

    [Fact]
    public async Task PlanForUser_classEntityNameMismatch_candidateIsSkipped_notGroundedOnWrongClass()
    {
        // The corpus only ever returns a "Warlock" class entity, never the requested "Barbarian".
        // The old fallback (`?? classResults[0]`) would have grounded the advice on Warlock's
        // hit die/features while mislabeling the candidate "Barbarian". The fix must skip the
        // candidate entirely rather than ground it on the wrong class.
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(FighterSheet());
        var service = BuildService(out _, new FakeWrongClassEntityRetrievalService());

        var advice = await service.PlanForUserAsync(
            snapshotId, ownerUserId, targetClass: "Barbarian", considerDip: true, default);

        // Non-vacuous: with targetClass="Barbarian" the only possible candidate is the Barbarian
        // dip (Fighter is filtered out by targetClass, Warlock is never a requested class), so an
        // empty result proves the mismatch was actually detected and the candidate was skipped —
        // not that the candidate list is empty for some unrelated reason.
        advice.Candidates.Should().BeEmpty();
        advice.Candidates.Should().NotContain(c => c.ClassName == "Barbarian");
    }


    [Fact]
    public async Task PlanForUser_corpusHasBothEditions_groundsOnEdition2014NotEdition2024()
    {
        // The corpus returns a wrong-edition (Edition2024, d8 hit die) Fighter entity FIRST and
        // the correct Edition2014 (d10 hit die) Fighter entity second, for the same name query.
        // The slot tables (MulticlassSlotTableSeeder) and MulticlassRules are PHB-2014-only, so
        // the service must pin the lookup to Edition2014 rather than taking whichever same-named
        // entity comes first. If the edition pin were removed, this test would ground HP math on
        // the 2024 (d8) entity and fail.
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(FighterSheet());
        var service = BuildService(out _, new FakeMultiEditionClassRetrievalService());

        var advice = await service.PlanForUserAsync(snapshotId, ownerUserId, null, considerDip: false, default);

        var fighter = advice.Candidates.Should().ContainSingle(c => c.ClassName == "Fighter" && !c.IsNewClassDip)
            .Subject;
        fighter.Delta.HpRollFormula.Should().Be("1d10",
            "the grounded class entity must be the Edition2014 Fighter (d10 hit die), not the Edition2024 one (d8)");
    }
}
