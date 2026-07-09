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
public sealed class ResolveSpellSaveDcTests(PostgresFixture pg) : IAsyncLifetime
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

    private async Task<ResolvedFact> ResolveForSheet(CharacterSheet sheet, string feature)
    {
        var dbf = DbFactory();

        await using var db = pg.NewContext();
        db.HeroSnapshots.Add(new HeroSnapshot(0, 1, 1, feature, sheet.Level, DateTime.UtcNow, sheet));
        await db.SaveChangesAsync();
        var snapshotId = await db.HeroSnapshots
            .Where(s => s.HeroId == 1)
            .Select(s => s.Id)
            .FirstAsync();

        var heroes = new HeroRepository(dbf);
        var svc = new CharacterResolutionService(dbf, heroes);
        return await svc.ResolveAsync(snapshotId, feature);
    }

    private Task<ResolvedFact> ResolveSpellSaveDcForSheet(CharacterSheet sheet) =>
        ResolveForSheet(sheet, "spell save dc");

    private Task<ResolvedFact> ResolveSpellAttackForSheet(CharacterSheet sheet) =>
        ResolveForSheet(sheet, "spell attack");

    // ─── save DC tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task Each_caster_class_reports_its_own_dc()
    {
        var sheet = new CharacterSheet
        {
            Classes = [new() { Class = "Cleric", Level = 3 }, new() { Class = "Wizard", Level = 2 }],
            Wisdom = 16,       // Cleric: mod +3
            Intelligence = 14, // Wizard: mod +2
        };
        // total level 5 -> PB +3
        var fact = await ResolveSpellSaveDcForSheet(sheet);

        fact.Components.Should().Contain(c => c.Label == "Cleric save DC" && c.Value == "14"); // 8+3+3
        fact.Components.Should().Contain(c => c.Label == "Wizard save DC" && c.Value == "13"); // 8+3+2
    }

    [Fact]
    public async Task Non_caster_reports_needs_review()
    {
        var sheet = new CharacterSheet { Classes = [new() { Class = "Barbarian", Level = 5 }] };
        var fact = await ResolveSpellSaveDcForSheet(sheet);
        fact.Confidence.Should().Be("needsReview");
    }

    // ─── spell attack tests ─────────────────────────────────────────────────

    [Fact]
    public async Task Each_caster_class_reports_its_own_attack()
    {
        var sheet = new CharacterSheet
        {
            Classes = [new() { Class = "Cleric", Level = 3 }, new() { Class = "Wizard", Level = 2 }],
            Wisdom = 16,       // Cleric: mod +3
            Intelligence = 14, // Wizard: mod +2
        };
        // total level 5 -> PB +3
        var fact = await ResolveSpellAttackForSheet(sheet);

        fact.Components.Should().Contain(c => c.Label == "Cleric spell attack" && c.Value == "+6"); // 3+3
        fact.Components.Should().Contain(c => c.Label == "Wizard spell attack" && c.Value == "+5"); // 3+2
    }

    [Fact]
    public async Task Non_caster_reports_needs_review_for_attack()
    {
        var sheet = new CharacterSheet { Classes = [new() { Class = "Barbarian", Level = 5 }] };
        var fact = await ResolveSpellAttackForSheet(sheet);
        fact.Confidence.Should().Be("needsReview");
    }
}