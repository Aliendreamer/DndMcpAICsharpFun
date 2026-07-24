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
public sealed class SpellCountResolutionIntegrationTests(PostgresFixture pg) : IAsyncLifetime
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

    private static CharacterSheet Sheet(string @class, int level, (string Ability, int Score) primary)
    {
        var s = new CharacterSheet { Classes = [new ClassLevel { Class = @class, Level = level }] };
        typeof(CharacterSheet).GetProperty(primary.Ability)!.SetValue(s, primary.Score);
        return s;
    }

    [Fact] // KNOWN caster: reads Spells Known cell with provenance
    public async Task Known_caster_reads_spells_known_from_table()
    {
        var dbf = DbFactory();
        var snapId = await SeedAsync(dbf, "bard", Sheet("Bard", 3, ("Charisma", 16)), heroId: 1);
        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "spell count");
        fact.Confidence.Should().Be("ok");
        var bard = fact.Components.First(c => c.Label == "Bard");
        bard.Value.Should().Contain("known");
        bard.Provenance.Should().NotBeNull(); // table-sourced
    }

    [Fact] // PREPARED full caster: mod + level, no provenance
    public async Task Prepared_full_caster_computes_mod_plus_level()
    {
        var dbf = DbFactory();
        var snapId = await SeedAsync(dbf, "cleric", Sheet("Cleric", 5, ("Wisdom", 16)), heroId: 2); // +3 mod
        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "spell count");
        var cleric = fact.Components.First(c => c.Label == "Cleric");
        cleric.Value.Should().Contain("8 prepared"); // 3 + 5
        cleric.Provenance.Should().BeNull(); // computed
    }

    [Fact] // PREPARED half caster (Paladin): mod + level/2, min 1
    public async Task Prepared_half_caster_uses_half_level()
    {
        var dbf = DbFactory();
        var snapId = await SeedAsync(dbf, "paladin", Sheet("Paladin", 6, ("Charisma", 14)), heroId: 3); // +2 mod
        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "spell count");
        fact.Components.First(c => c.Label == "Paladin").Value.Should().Contain("5 prepared"); // 2 + 6/2 = 5
    }

    [Fact] // NON-caster: no fabricated number
    public async Task Non_caster_contributes_no_spellcasting()
    {
        var dbf = DbFactory();
        var snapId = await SeedAsync(dbf, "fighter", Sheet("Fighter", 3, ("Strength", 16)), heroId: 4);
        var fact = await new CharacterResolutionService(dbf, new HeroRepository(dbf)).ResolveAsync(snapId, "spell count");
        fact.Components.First(c => c.Label == "Fighter").Value.Should().Be("no spellcasting");
    }
}