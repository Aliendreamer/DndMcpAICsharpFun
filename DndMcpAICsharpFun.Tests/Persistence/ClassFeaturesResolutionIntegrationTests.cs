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
public sealed class ClassFeaturesResolutionIntegrationTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private IDbContextFactory<AppDbContext> DbFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(pg.Container.GetConnectionString()));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    [Fact]
    public async Task Class_features_resolve_for_l6_fighter()
    {
        // Land the real projected Fighter class table.
        var dbf = DbFactory();
        var classTables = new FivetoolsTableProjection().BuildForBook(TestPaths.RepoFile("5etools"), "PHB");
        var fighter = classTables.Single(t => t.Id == "phb14.table.fighter");
        var file = new CanonicalJsonFile("1", new CanonicalBookMetadata("PHB", "Edition2014", "x", "PHB"), [], new[] { fighter }, []);
        await new StructuredFactProjector(dbf).ProjectAsync(file, CancellationToken.None);

        var sheet = new CharacterSheet { Classes = [new ClassLevel { Class = "Fighter", Level = 6 }] };
        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, 1, 1, "F6", 6, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        var snapId = await db.HeroSnapshots.Where(s => s.HeroId == 1).Select(s => s.Id).FirstAsync();

        var svc = new CharacterResolutionService(dbf, new HeroRepository(dbf));
        var fact = await svc.ResolveAsync(snapId, "class features");

        fact.Confidence.Should().Be("ok");
        fact.Value.Should().Contain("Extra Attack").And.Contain("Ability Score Improvement");
        fact.Value.Should().Contain("+3"); // L6 proficiency bonus
        fact.Components.Should().ContainSingle(c => c.Label == "Fighter");
    }
}