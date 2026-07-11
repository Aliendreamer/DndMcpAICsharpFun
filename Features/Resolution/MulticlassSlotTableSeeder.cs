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
    public const string HalfCasterTableId = "phb14.table.half-caster-slots";
    public const string ThirdCasterTableId = "phb14.table.third-caster-slots";
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

    // PHB Paladin/Ranger. L1 has no slots; half-casters cap at 5th-level spells.
    private static readonly int[][] HalfCasterSlots =
    [
        [0,0,0,0,0,0,0,0,0], [2,0,0,0,0,0,0,0,0], [3,0,0,0,0,0,0,0,0], [3,0,0,0,0,0,0,0,0],
        [4,2,0,0,0,0,0,0,0], [4,2,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0],
        [4,3,2,0,0,0,0,0,0], [4,3,2,0,0,0,0,0,0], [4,3,3,0,0,0,0,0,0], [4,3,3,0,0,0,0,0,0],
        [4,3,3,1,0,0,0,0,0], [4,3,3,1,0,0,0,0,0], [4,3,3,2,0,0,0,0,0], [4,3,3,2,0,0,0,0,0],
        [4,3,3,3,1,0,0,0,0], [4,3,3,3,1,0,0,0,0], [4,3,3,3,2,0,0,0,0], [4,3,3,3,2,0,0,0,0],
    ];

    // PHB Eldritch Knight / Arcane Trickster. No slots before class level 3; third-casters cap at 4th-level.
    private static readonly int[][] ThirdCasterSlots =
    [
        [0,0,0,0,0,0,0,0,0], [0,0,0,0,0,0,0,0,0], [2,0,0,0,0,0,0,0,0], [3,0,0,0,0,0,0,0,0],
        [3,0,0,0,0,0,0,0,0], [3,0,0,0,0,0,0,0,0], [4,2,0,0,0,0,0,0,0], [4,2,0,0,0,0,0,0,0],
        [4,2,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0],
        [4,3,2,0,0,0,0,0,0], [4,3,2,0,0,0,0,0,0], [4,3,2,0,0,0,0,0,0], [4,3,3,0,0,0,0,0,0],
        [4,3,3,0,0,0,0,0,0], [4,3,3,0,0,0,0,0,0], [4,3,3,1,0,0,0,0,0], [4,3,3,1,0,0,0,0,0],
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        await SeedTableAsync(db, TableId, "Multiclass Spellcaster", Slots, ct);
        await SeedTableAsync(db, HalfCasterTableId, "Half-Caster Slots", HalfCasterSlots, ct);
        await SeedTableAsync(db, ThirdCasterTableId, "Third-Caster Slots", ThirdCasterSlots, ct);
    }

    private static async Task SeedTableAsync(
        AppDbContext db, string id, string name, int[][] rows, CancellationToken ct)
    {
        var table = await db.StructuredTables.FirstOrDefaultAsync(t => t.CanonicalId == id, ct);
        var columns = new[] { "casterLevel", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        if (table is null)
        {
            table = new StructuredTable
            {
                CanonicalId = id,
                Name = name,
                ColumnsJson = JsonSerializer.Serialize(columns),
                SourceBook = "PHB",
            };
            db.StructuredTables.Add(table);
            await db.SaveChangesAsync(ct); // assign table.Id
        }

        // Idempotent: clear then reinsert rows.
        await db.StructuredTableRows.Where(r => r.TableId == table.Id).ExecuteDeleteAsync(ct);
        for (var i = 0; i < rows.Length; i++)
        {
            var cells = new List<CanonicalCell> { new((i + 1).ToString(), Prov) };
            cells.AddRange(rows[i].Select(n => new CanonicalCell(n.ToString(), Prov)));
            db.StructuredTableRows.Add(new StructuredTableRow
            {
                TableId = table.Id,
                RowIndex = i,
                CellsJson = JsonSerializer.Serialize(cells),
            });
        }
        await db.SaveChangesAsync(ct);
    }


    /// <summary>Per-spell-level (1..9) slot counts for a resolved caster source, or all-zero
    /// for a non-caster / out-of-range level. Reads the same PHB arrays this class seeds.</summary>
    public static int[] SlotsForCasterLevel(MulticlassSpellcasting.SlotSource src)
    {
        var table = src.Kind switch
        {
            "half" => HalfCasterSlots,
            "third" => ThirdCasterSlots,
            "multiclass" => Slots,
            _ => null,
        };
        if (table is null || src.Level < 1 || src.Level > table.Length)
            return new int[9];
        return (int[])table[src.Level - 1].Clone();
    }
}