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
public sealed class SubclassSpellsResolutionIntegrationTests(PostgresFixture pg) : IAsyncLifetime
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
    public async Task Subclass_spells_resolve_for_l5_life_cleric()
    {
        var dbf = DbFactory();
        var scTables = SubclassSpellsProjector.Project(TestPaths.RepoFile("5etools"), "PHB");
        var life = scTables.Single(t => t.Id == "phb14.table.life-domain-spells");
        var file = new CanonicalJsonFile("1", new CanonicalBookMetadata("PHB", "Edition2014", "x", "PHB"), [], new[] { life }, []);
        await new StructuredFactProjector(dbf).ProjectAsync(file, CancellationToken.None);

        var sheet = new CharacterSheet { Classes = [new ClassLevel { Class = "Cleric", Subclass = "Life Domain", Level = 5 }] };
        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, 2, 1, "Life5", 5, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        var snapId = await db.HeroSnapshots.Where(s => s.HeroId == 2).Select(s => s.Id).FirstAsync();

        var svc = new CharacterResolutionService(dbf, new HeroRepository(dbf));
        var fact = await svc.ResolveAsync(snapId, "subclass spells");

        fact.Confidence.Should().Be("ok");
        fact.Value.Should().Contain("bless").And.Contain("cure wounds")
            .And.Contain("lesser restoration").And.Contain("beacon of hope").And.Contain("revivify");
        fact.Value.Should().NotContain("death ward"); // L7 grant, excluded at L5
    }
}