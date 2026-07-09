using DndMcpAICsharpFun.Domain;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Tests.Persistence;

[Collection("postgres")]
public sealed class StructuredFactsPersistenceTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task StructuredTableRow_CellsJson_round_trips_through_the_text_column()
    {
        long tableId;

        await using (var db = pg.NewContext())
        {
            var table = new StructuredTable
            {
                CanonicalId = "phb.table.dragonborn-ancestry",
                Name = "Dragonborn Draconic Ancestry",
                ColumnsJson = """["Dragon","Damage Type","Breath Weapon"]""",
                SourceBook = "phb",
            };
            db.StructuredTables.Add(table);

            await db.SaveChangesAsync();
            tableId = table.Id;

            db.StructuredTableRows.AddRange(
                new StructuredTableRow
                {
                    TableId = tableId,
                    RowIndex = 0,
                    CellsJson = """["Black","Acid","5 by 30 ft. line (Dex. save)"]""",
                },
                new StructuredTableRow
                {
                    TableId = tableId,
                    RowIndex = 7,
                    CellsJson = """["Gold","Fire","15 ft. cone (Dex. save)"]""",
                });

            await db.SaveChangesAsync();
        }

        await using (var db = pg.NewContext())
        {
            var row = await db.StructuredTableRows
                .SingleAsync(r => r.TableId == tableId && r.RowIndex == 7);

            row.CellsJson.Should().Be("""["Gold","Fire","15 ft. cone (Dex. save)"]""");
        }
    }
}