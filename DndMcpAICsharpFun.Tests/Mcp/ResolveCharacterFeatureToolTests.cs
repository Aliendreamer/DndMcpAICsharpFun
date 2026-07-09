using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Resolution;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using DndMcpAICsharpFun.Tests;
using DndMcpAICsharpFun.Tests.Persistence;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Mcp;

/// <summary>
/// SEC-08: character-scoped resolution is enforced server-side via
/// <see cref="CharacterResolutionService.ResolveForUserAsync"/>, which loads a snapshot only when
/// it belongs to a campaign owned by the caller. These tests cover the positive path (owner can
/// resolve) and, critically, the negative security path (a different user CANNOT resolve another
/// user's snapshot — it throws <see cref="UnauthorizedAccessException"/> and returns no fact).
/// </summary>
[Collection("postgres")]
public sealed class ResolveCharacterFeatureToolTests(PostgresFixture pg) : IAsyncLifetime
{
    private static readonly string DragonbornSlicePath =
        TestPaths.RepoFile("books/canonical/dragonborn-slice.json");

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ─── helpers ────────────────────────────────────────────────────────────

    private IDbContextFactory<AppDbContext> DbFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(opts =>
            opts.UseNpgsql(pg.Container.GetConnectionString()));
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    private HeroRepository Heroes() => new(DbFactory());

    private CharacterResolutionService BuildService()
    {
        var dbf = DbFactory();
        var heroes = new HeroRepository(dbf);
        return new CharacterResolutionService(dbf, heroes);
    }

    /// <summary>
    /// Seeds a full ownership chain (User → Campaign → Hero → HeroSnapshot) and returns the
    /// snapshot id together with the id of the user that owns it.
    /// </summary>
    private async Task<(long SnapshotId, long OwnerUserId)> SeedOwnedSnapshotAsync(int level)
    {
        var sheet = new CharacterSheet
        {
            Race = "Dragonborn",
            Classes = [new ClassLevel { Level = level }],
            Constitution = 16,
            ResolvedChoices = new Dictionary<string, string>
            {
                ["ancestry"] = "phb14.choiceset.draconic-ancestry:Red",
            },
        };

        await using var db = pg.NewContext();

        var user = new User(0, $"owner-{Guid.NewGuid():N}", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var campaign = new DndMcpAICsharpFun.Domain.Campaign(0, user.Id, "Owned Campaign", "", DateTime.UtcNow);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();

        var hero = new Hero(0, campaign.Id, "Dragonborn Hero", DateTime.UtcNow);
        db.Heroes.Add(hero);
        await db.SaveChangesAsync();

        var snapshot = new HeroSnapshot(0, hero.Id, level, $"L{level}", level, DateTime.UtcNow, sheet);
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

    private async Task SeedStructuredFactsAsync()
    {
        var loader = new CanonicalJsonLoader();
        var dbf = DbFactory();
        var projector = new StructuredFactProjector(dbf);
        var file = await loader.LoadAsync(DragonbornSlicePath, CancellationToken.None);
        await projector.ProjectAsync(file, CancellationToken.None);
    }

    // ─── tests ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Confirms the container actually started (not skipped/mocked).
    /// </summary>
    [Fact]
    public void Postgres_container_is_running()
    {
        pg.Container.State.Should().Be(DotNet.Testcontainers.Containers.TestcontainersStates.Running,
            "Testcontainers must have started the Postgres container");
    }

    /// <summary>
    /// The OWNER of a Red Dragonborn L11 (Con 16) snapshot can resolve its breath weapon:
    /// 15 ft. cone of fire, Dexterity save DC 15, 3d6, with provenance and confidence "ok".
    /// </summary>
    [Fact]
    public async Task ResolveForUser_owner_RedDragonborn_Level11_returns_expected_fact()
    {
        // Arrange
        await SeedStructuredFactsAsync();
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(level: 11);
        var svc = BuildService();

        // Act
        var fact = await svc.ResolveForUserAsync(snapshotId, ownerUserId, "breath weapon");

        // Assert — value contains the key substrings
        fact.Confidence.Should().Be("ok");
        fact.Value.Should().Contain("fire", "Red dragon deals fire damage");
        fact.Value.Should().Contain("15 ft. cone", "Red dragon has cone breath");
        fact.Value.Should().Contain("Dexterity", "Red dragon breath requires Dex save");
        fact.Value.Should().Contain("DC 15", "Level 11 Con 16 → DC 15");
        fact.Value.Should().Contain("3d6", "Tier 3 (L11) → 3d6");

        // Assert — components carry provenance
        fact.Components.Should().HaveCountGreaterThanOrEqualTo(4);
        fact.Components.Any(c => c.Provenance is not null && !string.IsNullOrEmpty(c.Provenance.BlockId))
            .Should().BeTrue("at least one component must carry a blockId from the PHB source");
    }

    /// <summary>
    /// SEC-08 negative security test: a user who does NOT own the snapshot cannot resolve it.
    /// <see cref="CharacterResolutionService.ResolveForUserAsync"/> must throw
    /// <see cref="UnauthorizedAccessException"/> and return no fact, and the repository's
    /// ownership-scoped lookup must return null for the non-owner while returning the snapshot
    /// for the owner.
    /// </summary>
    [Fact]
    public async Task ResolveForUser_non_owner_throws_UnauthorizedAccess_and_returns_no_fact()
    {
        // Arrange — snapshot owned by user A; a different user B exists
        await SeedStructuredFactsAsync();
        var (snapshotId, ownerUserId) = await SeedOwnedSnapshotAsync(level: 11);
        var otherUserId = await SeedUserAsync();
        var svc = BuildService();
        var heroes = Heroes();

        // Act — user B attempts to resolve user A's snapshot
        var act = async () => await svc.ResolveForUserAsync(snapshotId, otherUserId, "breath weapon");

        // Assert — access is denied server-side; no fact is produced
        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        // Assert — the ownership-scoped lookup gates on the correct user
        (await heroes.GetSnapshotForUserAsync(snapshotId, otherUserId))
            .Should().BeNull("user B does not own the snapshot");
        (await heroes.GetSnapshotForUserAsync(snapshotId, ownerUserId))
            .Should().NotBeNull("user A owns the snapshot");
    }
}