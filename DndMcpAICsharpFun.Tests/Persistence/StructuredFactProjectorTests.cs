using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Resolution;
using DndMcpAICsharpFun.Tests;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Persistence;

[Collection("postgres")]
public sealed class StructuredFactProjectorTests(PostgresFixture pg) : IAsyncLifetime
{
    private static readonly string DragonbornSlicePath =
        TestPaths.RepoFile("books/canonical/dragonborn-slice.json");

    private const string AncestryTableId = "phb14.table.draconic-ancestry";

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private StructuredFactProjector CreateProjector()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<DndMcpAICsharpFun.Infrastructure.Persistence.AppDbContext>(options =>
            options.UseNpgsql(pg.Container.GetConnectionString()));
        var sp = services.BuildServiceProvider();
        return new StructuredFactProjector(
            sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<DndMcpAICsharpFun.Infrastructure.Persistence.AppDbContext>>());
    }

    [Fact]
    public async Task ProjectAsync_is_idempotent_and_row_count_matches_source()
    {
        // Arrange
        var loader = new CanonicalJsonLoader();
        var projector = CreateProjector();
        var file = await loader.LoadAsync(DragonbornSlicePath, CancellationToken.None);

        // Act — project twice
        var (t1, r1, c1) = await projector.ProjectAsync(file, CancellationToken.None);
        var (t2, r2, c2) = await projector.ProjectAsync(file, CancellationToken.None);

        // Assert — second run returns same counts as first
        t2.Should().Be(t1, "tables count must be identical on second run (idempotent)");
        r2.Should().Be(r1, "rows count must be identical on second run (idempotent)");
        c2.Should().Be(c1, "choice-sets count must be identical on second run (idempotent)");

        // Assert the ancestry table has exactly 10 rows in DB (not 20)
        await using var db = pg.NewContext();

        var ancestryTable = await db.StructuredTables
            .FirstAsync(t => t.CanonicalId == AncestryTableId);

        var allRows = await db.StructuredTableRows
            .Where(r => r.TableId == ancestryTable.Id)
            .ToListAsync();

        allRows.Should().HaveCount(10, "ancestry table has 10 rows; second run must not double-insert");

        // Assert row at index 7 contains "fire" and "15 ft. cone"
        var row7 = await db.StructuredTableRows
            .SingleAsync(r => r.TableId == ancestryTable.Id && r.RowIndex == 7);

        row7.CellsJson.Should().Contain("fire", "row 7 is Red dragon with fire damage");
        row7.CellsJson.Should().Contain("15 ft. cone", "row 7 breathes in a 15 ft. cone");
    }

    [Fact]
    public async Task ProjectAsync_removes_tables_and_choice_sets_dropped_from_the_file()
    {
        var projector = CreateProjector();
        var book = new CanonicalBookMetadata("PHB", "2014", "hash-orphan", "Player's Handbook");

        static CanonicalTable Table(string id) =>
            new(id, id, ["col"], [new CanonicalTableRow([new CanonicalCell("v", null)])]);
        static CanonicalChoiceSet Choice(string id, string tableId) =>
            new(id, id, [new CanonicalChoiceOption("k", tableId, 0, null)]);

        var full = new CanonicalJsonFile("1", book, [],
            [Table("phb14.table.keep"), Table("phb14.table.drop")],
            [Choice("phb14.choiceset.keep", "phb14.table.keep"),
             Choice("phb14.choiceset.drop", "phb14.table.drop")]);

        var reduced = new CanonicalJsonFile("1", book, [],
            [Table("phb14.table.keep")],
            [Choice("phb14.choiceset.keep", "phb14.table.keep")]);

        await projector.ProjectAsync(full, CancellationToken.None);
        await projector.ProjectAsync(reduced, CancellationToken.None);

        await using var db = pg.NewContext();

        (await db.StructuredTables.AnyAsync(t => t.CanonicalId == "phb14.table.drop"))
            .Should().BeFalse("the dropped table must be removed on re-projection");
        (await db.StructuredTables.AnyAsync(t => t.CanonicalId == "phb14.table.keep"))
            .Should().BeTrue("the retained table must survive");
        (await db.ChoiceSetRows.AnyAsync(c => c.CanonicalId == "phb14.choiceset.drop"))
            .Should().BeFalse("the dropped choice-set must be removed on re-projection");
        (await db.ChoiceSetRows.AnyAsync(c => c.CanonicalId == "phb14.choiceset.keep"))
            .Should().BeTrue("the retained choice-set must survive");

        var keepId = await db.StructuredTables
            .Where(t => t.CanonicalId == "phb14.table.keep").Select(t => t.Id).FirstAsync();
        (await db.StructuredTableRows.CountAsync(r => r.TableId == keepId))
            .Should().Be(1, "orphaned rows of the dropped table must not remain");
    }
}