using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Features.Resolution;

/// <summary>
/// Projects <see cref="CanonicalJsonFile"/> tables and choice-sets into the
/// <see cref="StructuredTable"/> / <see cref="StructuredTableRow"/> / <see cref="ChoiceSetRow"/>
/// Postgres entities.  Each call is idempotent: rows are deleted and re-inserted so a second
/// call with the same file produces the same DB state.
/// </summary>
public sealed class StructuredFactProjector(IDbContextFactory<AppDbContext> dbf)
{
    public async Task<(int Tables, int Rows, int ChoiceSets)> ProjectAsync(
        CanonicalJsonFile file,
        CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);

        var tableCount = 0;
        var rowCount = 0;

        foreach (var table in file.Tables)
        {
            // Upsert StructuredTable matched on CanonicalId
            var existing = await db.StructuredTables
                .FirstOrDefaultAsync(t => t.CanonicalId == table.Id, ct);

            long tableId;
            if (existing is null)
            {
                var newTable = new StructuredTable
                {
                    CanonicalId  = table.Id,
                    Name         = table.Name,
                    ColumnsJson  = JsonSerializer.Serialize(table.Columns),
                    SourceBook   = file.Book.SourceBook,
                };
                db.StructuredTables.Add(newTable);
                await db.SaveChangesAsync(ct);
                tableId = newTable.Id;
            }
            else
            {
                existing.Name        = table.Name;
                existing.ColumnsJson = JsonSerializer.Serialize(table.Columns);
                existing.SourceBook  = file.Book.SourceBook;
                await db.SaveChangesAsync(ct);
                tableId = existing.Id;
            }

            // Delete existing rows then re-insert (idempotent)
            await db.StructuredTableRows
                .Where(r => r.TableId == tableId)
                .ExecuteDeleteAsync(ct);

            for (var i = 0; i < table.Rows.Count; i++)
            {
                db.StructuredTableRows.Add(new StructuredTableRow
                {
                    TableId   = tableId,
                    RowIndex  = i,
                    CellsJson = JsonSerializer.Serialize(table.Rows[i].Cells),
                });
                rowCount++;
            }

            await db.SaveChangesAsync(ct);
            tableCount++;
        }

        var choiceSetCount = 0;

        foreach (var cs in file.ChoiceSets)
        {
            // Upsert ChoiceSetRow matched on CanonicalId
            var existing = await db.ChoiceSetRows
                .FirstOrDefaultAsync(c => c.CanonicalId == cs.Id, ct);

            if (existing is null)
            {
                db.ChoiceSetRows.Add(new ChoiceSetRow
                {
                    CanonicalId = cs.Id,
                    Name        = cs.Name,
                    OptionsJson = JsonSerializer.Serialize(cs.Options),
                });
            }
            else
            {
                existing.Name        = cs.Name;
                existing.OptionsJson = JsonSerializer.Serialize(cs.Options);
            }

            await db.SaveChangesAsync(ct);
            choiceSetCount++;
        }

        return (tableCount, rowCount, choiceSetCount);
    }
}
