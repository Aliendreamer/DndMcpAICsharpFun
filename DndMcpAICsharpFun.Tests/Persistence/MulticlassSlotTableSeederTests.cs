using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Resolution;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Persistence;

[Collection("postgres")]
public sealed class MulticlassSlotTableSeederTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private MulticlassSlotTableSeeder CreateSeeder()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<DndMcpAICsharpFun.Infrastructure.Persistence.AppDbContext>(options =>
            options.UseNpgsql(pg.Container.GetConnectionString()));
        var sp = services.BuildServiceProvider();
        return new MulticlassSlotTableSeeder(
            sp.GetRequiredService<IDbContextFactory<DndMcpAICsharpFun.Infrastructure.Persistence.AppDbContext>>());
    }

    [Fact]
    public async Task Seeds_20_rows_and_cites_phb_and_is_idempotent()
    {
        var seeder = CreateSeeder();
        await seeder.SeedAsync(default);
        await seeder.SeedAsync(default); // second call must not duplicate

        await using var db = pg.NewContext();
        var table = await db.StructuredTables
            .SingleAsync(t => t.CanonicalId == MulticlassSlotTableSeeder.TableId);
        var rows = await db.StructuredTableRows
            .Where(r => r.TableId == table.Id).OrderBy(r => r.RowIndex).ToListAsync();

        rows.Should().HaveCount(20);

        // Combined caster level 5 -> row index 4: 4/3/2 first/second/third-level slots.
        var cells = JsonSerializer.Deserialize<List<CanonicalCell>>(rows[4].CellsJson)!;
        cells[0].Value.Should().Be("5");   // casterLevel column
        cells[1].Value.Should().Be("4");   // 1st-level slots
        cells[2].Value.Should().Be("3");   // 2nd-level slots
        cells[3].Value.Should().Be("2");   // 3rd-level slots
        cells[1].Provenance!.SourceBook.Should().Be("PHB");
    }


    [Fact]
    public async Task Seeds_half_caster_table_with_20_rows_and_cites_phb()
    {
        var seeder = CreateSeeder();
        await seeder.SeedAsync(default);
        await seeder.SeedAsync(default); // second call must not duplicate

        await using var db = pg.NewContext();
        var table = await db.StructuredTables
            .SingleAsync(t => t.CanonicalId == MulticlassSlotTableSeeder.HalfCasterTableId);
        var rows = await db.StructuredTableRows
            .Where(r => r.TableId == table.Id).OrderBy(r => r.RowIndex).ToListAsync();

        rows.Should().HaveCount(20);

        // Paladin/Ranger level 5 -> row index 4: 4/2 first/second-level slots.
        var cells = JsonSerializer.Deserialize<List<CanonicalCell>>(rows[4].CellsJson)!;
        cells[0].Value.Should().Be("5");   // casterLevel column
        cells[1].Value.Should().Be("4");   // 1st-level slots
        cells[2].Value.Should().Be("2");   // 2nd-level slots
        cells[1].Provenance!.SourceBook.Should().Be("PHB");
    }

    [Fact]
    public async Task Seeds_third_caster_table_with_20_rows_and_cites_phb()
    {
        var seeder = CreateSeeder();
        await seeder.SeedAsync(default);
        await seeder.SeedAsync(default); // second call must not duplicate

        await using var db = pg.NewContext();
        var table = await db.StructuredTables
            .SingleAsync(t => t.CanonicalId == MulticlassSlotTableSeeder.ThirdCasterTableId);
        var rows = await db.StructuredTableRows
            .Where(r => r.TableId == table.Id).OrderBy(r => r.RowIndex).ToListAsync();

        rows.Should().HaveCount(20);

        // Eldritch Knight/Arcane Trickster level 7 -> row index 6: 4/2 first/second-level slots.
        var cells = JsonSerializer.Deserialize<List<CanonicalCell>>(rows[6].CellsJson)!;
        cells[0].Value.Should().Be("7");   // casterLevel column
        cells[1].Value.Should().Be("4");   // 1st-level slots
        cells[2].Value.Should().Be("2");   // 2nd-level slots
        cells[1].Provenance!.SourceBook.Should().Be("PHB");
    }
}
