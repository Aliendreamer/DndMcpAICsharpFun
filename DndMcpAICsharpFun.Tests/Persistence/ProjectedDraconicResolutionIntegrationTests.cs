using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Resolution;
using DndMcpAICsharpFun.Infrastructure.Persistence;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Persistence;

[Collection("postgres")]
public sealed class ProjectedDraconicResolutionIntegrationTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private IDbContextFactory<AppDbContext> DbFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(opts => opts.UseNpgsql(pg.Container.GetConnectionString()));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    private CharacterResolutionService BuildService()
    {
        var dbf = DbFactory();
        return new CharacterResolutionService(dbf, new HeroRepository(dbf));
    }

    private async Task ProjectRealDraconicArtifactsAsync()
    {
        var artifacts = DraconicAncestryResolutionProjector.Project(TestPaths.RepoFile("5etools"), "PHB");
        artifacts.Tables.Should().Contain(t => t.Id == "phb14.table.draconic-ancestry",
            "the real 5etools PHB Dragonborn must project a normalized draconic table");
        var file = new CanonicalJsonFile(
            "1",
            new CanonicalBookMetadata("PHB", "Edition2014", "x", "PlayerHandbook 2014"),
            [],
            artifacts.Tables,
            artifacts.ChoiceSets);
        await new StructuredFactProjector(DbFactory()).ProjectAsync(file, CancellationToken.None);
    }

    private async Task<long> SeedBlackDragonbornAsync(int level)
    {
        var sheet = new CharacterSheet
        {
            Race = "Dragonborn",
            Classes = [new ClassLevel { Level = level }],
            Constitution = 16,
            ResolvedChoices = new Dictionary<string, string>
            {
                ["ancestry"] = "phb14.choiceset.draconic-ancestry:Black",
            },
        };
        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, 1, level, $"L{level}", level, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        return await db.HeroSnapshots.Where(s => s.HeroId == 1 && s.SessionNumber == level).Select(s => s.Id).FirstAsync();
    }

    [Fact]
    public async Task Projected_artifacts_resolve_black_dragonborn_breath_weapon()
    {
        await ProjectRealDraconicArtifactsAsync();
        var snapshotId = await SeedBlackDragonbornAsync(level: 5);
        var svc = BuildService();

        var fact = await svc.ResolveAsync(snapshotId, "breath weapon");

        fact.Confidence.Should().Be("ok", "the projected normalized table + choiceset fully drive resolution");
        fact.Value.Should().Contain("5 by 30 ft. line");
        fact.Value.Should().Contain("acid");
        fact.Value.Should().Contain("Dexterity");
        fact.Value.Should().Contain("DC 14", "L5 Con16 → 8 + 3 prof + 3 con");
        fact.Value.Should().Contain("1d10", "tier 1 (L5) → 1d10");
        fact.Components.Count(c => c.Provenance is not null).Should().BeGreaterThanOrEqualTo(3);
    }
}
