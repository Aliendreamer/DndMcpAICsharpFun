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
public sealed class ClassResourcesResolutionIntegrationTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private IDbContextFactory<AppDbContext> DbFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(pg.Container.GetConnectionString()));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    private async Task<long> SeedAsync(IDbContextFactory<AppDbContext> dbf, string classSlug, CharacterSheet sheet, long heroId)
    {
        var classTables = new FivetoolsTableProjection().BuildForBook(TestPaths.RepoFile("5etools"), "PHB");
        var table = classTables.Single(t => t.Id == $"phb14.table.{classSlug}");
        var file = new CanonicalJsonFile("1", new CanonicalBookMetadata("PHB", "Edition2014", "x", "PHB"), [], new[] { table }, []);
        await new StructuredFactProjector(dbf).ProjectAsync(file, CancellationToken.None);
        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, heroId, 1, "H", sheet.Level, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        return await db.HeroSnapshots.Where(s => s.HeroId == heroId).Select(s => s.Id).FirstAsync();
    }

    [Fact]
    public async Task Barbarian_rages_resolve_at_level_with_provenance()
    {
        var dbf = DbFactory();
        var sheet = new CharacterSheet { Classes = [new ClassLevel { Class = "Barbarian", Level = 5 }] };
        var snapId = await SeedAsync(dbf, "barbarian", sheet, heroId: 1);

        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "class resources");

        fact.Confidence.Should().Be("ok");
        fact.Components.Should().Contain(c => c.Label == "Barbarian Rages" && !string.IsNullOrWhiteSpace(c.Value));
        fact.Components.First(c => c.Label == "Barbarian Rages").Provenance.Should().NotBeNull();
        fact.Components.Should().NotContain(c => c.Label.Contains("Features") || c.Label.Contains("Proficiency Bonus"));
    }

    [Fact]
    public async Task Fighter_with_no_resource_column_resolves_none_not_fabricated()
    {
        var dbf = DbFactory();
        var sheet = new CharacterSheet { Classes = [new ClassLevel { Class = "Fighter", Level = 6 }] };
        var snapId = await SeedAsync(dbf, "fighter", sheet, heroId: 2);

        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "class resources");

        fact.Confidence.Should().Be("ok");
        fact.Components.Should().ContainSingle(c => c.Label == "Fighter" && c.Value == "none");
    }

    [Fact]
    public async Task Missing_class_table_is_needsReview()
    {
        var dbf = DbFactory(); // no projection seeded
        var sheet = new CharacterSheet { Classes = [new ClassLevel { Class = "Barbarian", Level = 5 }] };
        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, 3, 1, "H", 5, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        var snapId = await db.HeroSnapshots.Where(s => s.HeroId == 3).Select(s => s.Id).FirstAsync();

        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "class resources");
        fact.Confidence.Should().Be("needsReview");
    }
}