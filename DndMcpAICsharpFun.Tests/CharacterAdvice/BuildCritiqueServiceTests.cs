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
/// Fake entity retrieval: for a Type=Class query for "Fighter" (edition-pinned Edition2014),
/// always returns a canned PHB Fighter <c>ClassFields</c> entity with a level-3 subclass slot
/// ("Martial Archetype") and a level-5 "Extra Attack" feature. Every other class query (and every
/// non-Class query) returns empty, since the (B)/(C) finding tests don't need a class entity.
/// </summary>
file sealed class FakeFighterEntityRetrievalService : IEntityRetrievalService
{
    public Task<EntityFullResult?> GetByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult<EntityFullResult?>(null);

    public Task<IList<EntitySearchResult>> SearchAsync(EntitySearchQuery query, CancellationToken ct) =>
        Task.FromResult<IList<EntitySearchResult>>([]);

    public Task<IList<EntityDiagnosticResult>> SearchDiagnosticAsync(EntitySearchQuery query, CancellationToken ct)
    {
        if (query.Type != EntityType.Class
            || !string.Equals(query.QueryText, "Fighter", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<IList<EntityDiagnosticResult>>([]);

        using var doc = JsonDocument.Parse("""
            {
              "hd": { "number": 1, "faces": 10 },
              "classFeatures": [
                "Fighting Style|Fighter|PHB|1",
                "Second Wind|Fighter|PHB|1",
                "Martial Archetype|Fighter|PHB|3",
                "Extra Attack|Fighter|PHB|5"
              ],
              "subclassTitle": "Martial Archetype"
            }
            """);
        var result = new EntityDiagnosticResult(
            Id: "phb14.class.fighter",
            Type: EntityType.Class,
            Name: "Fighter",
            SourceBook: "PHB",
            Edition: "Edition2014",
            Page: 72,
            SettingTags: [],
            PointId: "point-fighter",
            Fields: doc.RootElement.Clone(),
            Score: 1.0f);
        return Task.FromResult<IList<EntityDiagnosticResult>>([result]);
    }
}

/// <summary>
/// SEC-08-style coverage for <see cref="BuildCritiqueService"/>: ownership gating (SHIP BLOCKER),
/// the (A) untaken-choice findings (missing feature, subclass not chosen, formatting-variant
/// non-false-positive), the (B) stat-consistency finding, the (C) ability-alignment finding, and
/// the clean-build empty-findings case.
/// </summary>
[Collection("postgres")]
public sealed class BuildCritiqueServiceTests(PostgresFixture pg) : IAsyncLifetime
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

    private BuildCritiqueService BuildService(out HeroRepository heroes)
    {
        var dbf = DbFactory();
        heroes = new HeroRepository(dbf);
        return new BuildCritiqueService(heroes, new FakeFighterEntityRetrievalService());
    }

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

    private static CharacterSheet FighterSheet(int level, string subclass, params string[] featureNames) => new()
    {
        Classes = [new ClassLevel { Class = "Fighter", Subclass = subclass, Level = level }],
        Strength = 15,
        Constitution = 14,
        Dexterity = 8,
        Intelligence = 8,
        Wisdom = 8,
        Charisma = 8,
        Features = [.. featureNames.Select(n => new CharacterFeature { Name = n })],
    };

    [Fact] // ownership — SHIP BLOCKER
    public async Task Critique_otherUsersSnapshot_throws()
    {
        var (snapshotId, _) = await SeedOwnedSnapshotAsync(
            FighterSheet(3, "Champion", "Fighting Style", "Second Wind"));
        var otherUserId = await SeedUserAsync();
        var service = BuildService(out _);

        await FluentActions.Awaiting(() => service.CritiqueForUserAsync(snapshotId, otherUserId, default))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact] // (A) missing feature
    public async Task Level5Fighter_missingExtraAttack_isFlagged()
    {
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(
            FighterSheet(5, "Champion", "Fighting Style", "Second Wind"));
        var service = BuildService(out _);

        var c = await service.CritiqueForUserAsync(snapshotId, ownerUserId, default);

        c.Findings.Should().Contain(f => f.Kind == CritiqueKind.UntakenChoice
            && f.Observation.Contains("Extra Attack"));
    }

    [Fact] // (A) subclass not chosen
    public async Task Fighter3_noSubclass_isFlagged()
    {
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(
            FighterSheet(3, "", "Fighting Style", "Second Wind"));
        var service = BuildService(out _);

        var c = await service.CritiqueForUserAsync(snapshotId, ownerUserId, default);

        c.Findings.Should().Contain(f => f.Kind == CritiqueKind.UntakenChoice
            && f.Observation.Contains("subclass", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // (A) formatting variant NOT a false positive
    public async Task FeatureNameFormattingVariant_isNotMissing()
    {
        // "Extra-Attack" (hyphen, not space) normalizes identically to "Extra Attack" via
        // EntityNameIndex.Normalize (which keeps only letters/digits), so it must count as present.
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(
            FighterSheet(5, "Champion", "Fighting Style", "Second Wind", "Extra-Attack"));
        var service = BuildService(out _);

        var c = await service.CritiqueForUserAsync(snapshotId, ownerUserId, default);

        c.Findings.Should().NotContain(f => f.Kind == CritiqueKind.UntakenChoice
            && f.Observation.Contains("Extra Attack"));
    }

    [Fact] // (B) stat mismatch
    public async Task RecordedSaveDcDiffersFromComputed_isFlagged()
    {
        // Cleric 3: Wisdom 16 (+3), PB +2 → computed DC 8+2+3=13; recorded 11 (wrong).
        // Attack bonus and spell slots are set to their computed values so only the DC mismatch fires.
        var sheet = new CharacterSheet
        {
            Classes = [new ClassLevel { Class = "Cleric", Subclass = "Life", Level = 3 }],
            Strength = 10,
            Constitution = 12,
            Dexterity = 10,
            Intelligence = 10,
            Wisdom = 16,
            Charisma = 10,
            SpellcastingAbility = "Wisdom",
            SpellSaveDC = 11,
            SpellAttackBonus = 5,
            SpellSlots = [4, 2, 0, 0, 0, 0, 0, 0, 0],
        };
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(sheet);
        var service = BuildService(out _);

        var c = await service.CritiqueForUserAsync(snapshotId, ownerUserId, default);

        c.Findings.Should().Contain(f => f.Kind == CritiqueKind.StatConsistency);
    }

    [Fact] // (C) ability misalignment
    public async Task CasterHighestAbilityNotCastingAbility_isFlagged()
    {
        // Wizard casts on Intelligence, but Strength (16) outranks Intelligence (12) on the sheet.
        var sheet = new CharacterSheet
        {
            Classes = [new ClassLevel { Class = "Wizard", Subclass = "", Level = 3 }],
            Strength = 16,
            Constitution = 12,
            Dexterity = 10,
            Intelligence = 12,
            Wisdom = 10,
            Charisma = 10,
        };
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(sheet);
        var service = BuildService(out _);

        var c = await service.CritiqueForUserAsync(snapshotId, ownerUserId, default);

        c.Findings.Should().Contain(f => f.Kind == CritiqueKind.AbilityAlignment);
    }

    [Fact] // clean build → no findings
    public async Task CleanBuild_hasNoFindings()
    {
        var sheet = FighterSheet(5, "Champion", "Fighting Style", "Second Wind", "Extra Attack");
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(sheet);
        var service = BuildService(out _);

        var c = await service.CritiqueForUserAsync(snapshotId, ownerUserId, default);

        c.Findings.Should().BeEmpty();
    }
}
