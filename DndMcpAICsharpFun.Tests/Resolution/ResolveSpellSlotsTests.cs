using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Resolution;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Resolution;

[Collection("postgres")]
public sealed class ResolveSpellSlotsTests(PostgresFixture pg) : IAsyncLifetime
{
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

    private async Task<ResolvedFact> ResolveSpellSlotsForSheet(CharacterSheet sheet)
    {
        var dbf = DbFactory();
        await new MulticlassSlotTableSeeder(dbf).SeedAsync(default);

        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, 1, 1, "SpellSlots", sheet.Level, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        var snapshotId = await db.HeroSnapshots
            .Where(s => s.HeroId == 1)
            .Select(s => s.Id)
            .FirstAsync();

        var heroes = new HeroRepository(dbf);
        var svc = new CharacterResolutionService(dbf, heroes);
        return await svc.ResolveAsync(snapshotId, "spell slots");
    }

    // ─── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Multiclass_caster_reads_combined_level_row_and_cites_the_table()
    {
        var sheet = new CharacterSheet
        {
            Classes = [new() { Class = "Paladin", Level = 6 }, new() { Class = "Sorcerer", Level = 2 }],
            Charisma = 16,
        };

        var fact = await ResolveSpellSlotsForSheet(sheet);

        fact.Feature.Should().Be("spell slots");
        // combined caster level 5 -> 4/3/2 first/second/third
        fact.Components.Should().Contain(c => c.Label == "level 1 slots" && c.Value == "4");
        fact.Components.Should().Contain(c => c.Label == "level 3 slots" && c.Value == "2");
        fact.Components.First(c => c.Label == "level 1 slots").Provenance!.SourceBook.Should().Be("PHB");
        fact.Confidence.Should().Be("ok");
    }

    [Fact]
    public async Task Warlock_multiclass_reports_pact_separately()
    {
        var sheet = new CharacterSheet
        {
            Classes = [new() { Class = "Warlock", Level = 3 }, new() { Class = "Sorcerer", Level = 2 }],
            Charisma = 16,
        };

        var fact = await ResolveSpellSlotsForSheet(sheet);

        // combined level counts only the Sorcerer (2) -> 3/0 slots
        fact.Components.Should().Contain(c => c.Label == "level 1 slots" && c.Value == "3");
        // pact is separate and carries no table provenance
        var pact = fact.Components.Single(c => c.Label == "pact magic");
        pact.Value.Should().Contain("2");  // 2 slots at 2nd level
        pact.Provenance.Should().BeNull();
    }

    [Fact]
    public async Task Non_caster_multiclass_has_no_spell_slots()
    {
        var sheet = new CharacterSheet
        {
            Classes = [new() { Class = "Rogue", Level = 3 }, new() { Class = "Fighter", Level = 2 }],
        };

        var fact = await ResolveSpellSlotsForSheet(sheet);

        fact.Value.Should().Contain("no spellcasting");
        fact.Confidence.Should().Be("needsReview");
    }
}
