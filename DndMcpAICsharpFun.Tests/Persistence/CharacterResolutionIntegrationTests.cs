using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Resolution;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using DndMcpAICsharpFun.Tests;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Persistence;

[Collection("postgres")]
public sealed class CharacterResolutionIntegrationTests(PostgresFixture pg) : IAsyncLifetime
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

    private CharacterResolutionService BuildService()
    {
        var dbf = DbFactory();
        var heroes = new HeroRepository(dbf);
        return new CharacterResolutionService(dbf, heroes);
    }

    private async Task<long> SeedSnapshotAsync(int level)
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
        db.HeroSnapshots.Add(new HeroSnapshot(0, 1, level, $"L{level}", level, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        return await db.HeroSnapshots
            .Where(s => s.HeroId == 1 && s.SessionNumber == level)
            .Select(s => s.Id)
            .FirstAsync();
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
    /// Red Dragonborn, Level 11, Con 16:
    ///   breath weapon → "15 ft. cone of fire, Dexterity save DC 15, 3d6"
    /// </summary>
    [Fact]
    public async Task ResolveAsync_RedDragonborn_Level11_returns_expected_breath_weapon()
    {
        // Arrange
        await SeedStructuredFactsAsync();
        var snapshotId = await SeedSnapshotAsync(level: 11);
        var svc = BuildService();

        // Act
        var fact = await svc.ResolveAsync(snapshotId, "breath weapon");

        // Assert value contains the key substrings
        fact.Confidence.Should().Be("ok");
        fact.Value.Should().Contain("fire", "Red dragon deals fire damage");
        fact.Value.Should().Contain("15 ft. cone", "Red dragon has cone breath");
        fact.Value.Should().Contain("Dexterity", "Red dragon breath requires Dex save");
        fact.Value.Should().Contain("DC 15", "Level 11 Con 16 → DC 15");
        fact.Value.Should().Contain("3d6", "Tier 3 (L11) → 3d6");

        // Assert ≥4 components
        fact.Components.Should().HaveCountGreaterThanOrEqualTo(4);

        // Assert ≥3 components have non-null provenance
        fact.Components.Count(c => c.Provenance is not null)
            .Should().BeGreaterThanOrEqualTo(3, "most cells carry PHB provenance");
    }

    /// <summary>
    /// Red Dragonborn, Level 3, Con 16:
    ///   dice = 1d10, DC = 8 + 2 (prof) + 3 (con) = 13
    /// </summary>
    [Fact]
    public async Task ResolveAsync_RedDragonborn_Level3_returns_1d10_dice()
    {
        // Arrange
        await SeedStructuredFactsAsync();
        var snapshotId = await SeedSnapshotAsync(level: 3);
        var svc = BuildService();

        // Act
        var fact = await svc.ResolveAsync(snapshotId, "breath weapon");

        // Assert
        fact.Confidence.Should().Be("ok");
        fact.Value.Should().Contain("1d10", "Tier 1 (L3) → 1d10");
        fact.Value.Should().Contain("DC 13", "Level 3 Con 16 → DC 13 (8+2+3)");
    }

    /// <summary>
    /// Red Dragonborn, Level 17, Con 16:
    ///   dice = 4d6, DC = 8 + 6 (prof) + 3 (con) = 17
    /// </summary>
    [Fact]
    public async Task ResolveAsync_RedDragonborn_Level17_returns_4d6_dice()
    {
        // Arrange
        await SeedStructuredFactsAsync();
        var snapshotId = await SeedSnapshotAsync(level: 17);
        var svc = BuildService();

        // Act
        var fact = await svc.ResolveAsync(snapshotId, "breath weapon");

        // Assert
        fact.Confidence.Should().Be("ok");
        fact.Value.Should().Contain("4d6", "Tier 4 (L17) → 4d6");
        fact.Value.Should().Contain("DC 17", "Level 17 Con 16 → DC 17 (8+6+3)");
    }

    /// <summary>
    /// When there is no ancestry choice, the service returns needsReview.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_missing_ancestry_returns_needsReview()
    {
        // Arrange — snapshot with no ancestry choice
        await using (var db = pg.NewContext())
        {
            var sheet = new CharacterSheet { Race = "Dragonborn", Constitution = 14, Classes = [new ClassLevel { Level = 5 }] };
            db.HeroSnapshots.Add(new HeroSnapshot(0, 2, 1, "NoAncestry", 5, DateTime.UtcNow, sheet));
            await db.SaveChangesAsync();
        }

        long snapshotId;
        await using (var db = pg.NewContext())
        {
            snapshotId = await db.HeroSnapshots
                .Where(s => s.HeroId == 2)
                .Select(s => s.Id)
                .FirstAsync();
        }

        var svc = BuildService();

        // Act
        var fact = await svc.ResolveAsync(snapshotId, "breath weapon");

        // Assert
        fact.Confidence.Should().Be("needsReview");
        fact.Value.Should().Be("unknown");
    }

    /// <summary>
    /// Unsupported feature throws NotSupportedException.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_unsupported_feature_throws()
    {
        // Arrange — seed minimal snapshot
        await using (var db = pg.NewContext())
        {
            var sheet = new CharacterSheet { Classes = [new ClassLevel { Level = 1 }] };
            db.HeroSnapshots.Add(new HeroSnapshot(0, 3, 1, "UnsupportedFeature", 1, DateTime.UtcNow, sheet));
            await db.SaveChangesAsync();
        }

        long snapshotId;
        await using (var db = pg.NewContext())
        {
            snapshotId = await db.HeroSnapshots
                .Where(s => s.HeroId == 3)
                .Select(s => s.Id)
                .FirstAsync();
        }

        var svc = BuildService();

        // Act
        var act = async () => await svc.ResolveAsync(snapshotId, "fireball");

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*fireball*");
    }
}