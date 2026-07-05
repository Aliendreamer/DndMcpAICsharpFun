using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Features.Resolution;

/// <summary>
/// Idempotently seeds the PHB Multiclass Spellcaster combined-level → spell-slots table into
/// <see cref="StructuredTable"/> so multiclass slot resolution can cite a real provenance ref,
/// exactly as slice-1's breath-weapon path cites its projected table.
/// </summary>
public sealed class MulticlassSlotTableSeeder(IDbContextFactory<AppDbContext> dbf)
{
    public const string TableId = "phb14.table.multiclass-spellcaster";
    private static readonly ProvenanceRef Prov = new("phb14.block.multiclassing", "PHB", 164);

    // PHB p.164. Row i (0-based) = combined caster level i+1. Columns: slots for spell levels 1..9.
    private static readonly int[][] Slots =
    [
        [2,0,0,0,0,0,0,0,0], [3,0,0,0,0,0,0,0,0], [4,2,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0],
        [4,3,2,0,0,0,0,0,0], [4,3,3,0,0,0,0,0,0], [4,3,3,1,0,0,0,0,0], [4,3,3,2,0,0,0,0,0],
        [4,3,3,3,1,0,0,0,0], [4,3,3,3,2,0,0,0,0], [4,3,3,3,2,1,0,0,0], [4,3,3,3,2,1,0,0,0],
        [4,3,3,3,2,1,1,0,0], [4,3,3,3,2,1,1,0,0], [4,3,3,3,2,1,1,1,0], [4,3,3,3,2,1,1,1,0],
        [4,3,3,3,2,1,1,1,1], [4,3,3,3,3,1,1,1,1], [4,3,3,3,3,2,1,1,1], [4,3,3,3,3,2,2,1,1],
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);

        var table = await db.StructuredTables.FirstOrDefaultAsync(t => t.CanonicalId == TableId, ct);
        var columns = new[] { "casterLevel", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        if (table is null)
        {
            table = new StructuredTable
            {
                CanonicalId = TableId,
                Name = "Multiclass Spellcaster",
                ColumnsJson = JsonSerializer.Serialize(columns),
                SourceBook = "PHB",
            };
            db.StructuredTables.Add(table);
            await db.SaveChangesAsync(ct); // assign table.Id
        }

        // Idempotent: clear then reinsert rows.
        await db.StructuredTableRows.Where(r => r.TableId == table.Id).ExecuteDeleteAsync(ct);
        for (var i = 0; i < Slots.Length; i++)
        {
            var cells = new List<CanonicalCell> { new((i + 1).ToString(), Prov) };
            cells.AddRange(Slots[i].Select(n => new CanonicalCell(n.ToString(), Prov)));
            db.StructuredTableRows.Add(new StructuredTableRow
            {
                TableId = table.Id,
                RowIndex = i,
                CellsJson = JsonSerializer.Serialize(cells),
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
