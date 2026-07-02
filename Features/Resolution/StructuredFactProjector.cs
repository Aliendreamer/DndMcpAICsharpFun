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
/// call with the same file produces the same DB state. Tables/choice-sets dropped from the file
/// (relative to a prior projection) are removed from the DB as well, so orphans do not accumulate.
/// </summary>
public sealed class StructuredFactProjector(IDbContextFactory<AppDbContext> dbf)
{
    public async Task<(int Tables, int Rows, int ChoiceSets)> ProjectAsync(
        CanonicalJsonFile file,
        CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);

        var currentTableIds = new HashSet<string>();
        var newTables = new List<(CanonicalTable Source, StructuredTable Entity)>();

        // Phase 1: upsert every table. New tables are added (Ids assigned by the single save below);
        // existing tables are updated in place. No per-table save (NET-07).
        foreach (var table in file.Tables)
        {
            currentTableIds.Add(table.Id);
            var existing = await db.StructuredTables
                .FirstOrDefaultAsync(t => t.CanonicalId == table.Id, ct);

            if (existing is null)
            {
                var newTable = new StructuredTable
                {
                    CanonicalId = table.Id,
                    Name        = table.Name,
                    ColumnsJson = JsonSerializer.Serialize(table.Columns),
                    SourceBook  = file.Book.SourceBook,
                };
                db.StructuredTables.Add(newTable);
                newTables.Add((table, newTable));
            }
            else
            {
                existing.Name        = table.Name;
                existing.ColumnsJson = JsonSerializer.Serialize(table.Columns);
                existing.SourceBook  = file.Book.SourceBook;
            }
        }

        // COR-21: drop tables for this book that are no longer in the file (rows first, then tables).
        var orphanTableIds = await db.StructuredTables
            .Where(t => t.SourceBook == file.Book.SourceBook && !currentTableIds.Contains(t.CanonicalId))
            .Select(t => t.Id)
            .ToListAsync(ct);
        if (orphanTableIds.Count > 0)
        {
            await db.StructuredTableRows
                .Where(r => orphanTableIds.Contains(r.TableId))
                .ExecuteDeleteAsync(ct);
            await db.StructuredTables
                .Where(t => orphanTableIds.Contains(t.Id))
                .ExecuteDeleteAsync(ct);
        }

        // NET-07: one save assigns all new-table Ids and persists the existing-table updates.
        await db.SaveChangesAsync(ct);

        // Resolve canonical-id -> table PK (new entities now have Ids populated).
        var tableIdByCanonical = new Dictionary<string, long>();
        foreach (var (source, entity) in newTables)
            tableIdByCanonical[source.Id] = entity.Id;

        var rowCount = 0;
        foreach (var table in file.Tables)
        {
            var tableId = tableIdByCanonical.TryGetValue(table.Id, out var id)
                ? id
                : await db.StructuredTables
                    .Where(t => t.CanonicalId == table.Id)
                    .Select(t => t.Id)
                    .FirstAsync(ct);

            // Delete existing rows then re-insert (idempotent).
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
        }

        // NET-07: one save for every row across every table.
        await db.SaveChangesAsync(ct);
        var tableCount = file.Tables.Count;

        // Choice-sets: upsert all, no per-item save (NET-07).
        var currentChoiceSetIds = new HashSet<string>();
        foreach (var cs in file.ChoiceSets)
        {
            currentChoiceSetIds.Add(cs.Id);
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
        }

        // COR-21: drop choice-sets for this book no longer in the file. ChoiceSetRow has no SourceBook
        // column, so scope by the canonical-id slug prefix (e.g. "phb14.") shared by all of a book's ids.
        var slug = file.Tables.Select(t => t.Id)
            .Concat(file.ChoiceSets.Select(c => c.Id))
            .Select(entityId => entityId.Split('.', 2)[0])
            .FirstOrDefault();
        if (slug is not null)
        {
            var prefix = slug + ".";
            await db.ChoiceSetRows
                .Where(c => c.CanonicalId.StartsWith(prefix) && !currentChoiceSetIds.Contains(c.CanonicalId))
                .ExecuteDeleteAsync(ct);
        }

        // NET-07: one save for all choice-set inserts/updates.
        await db.SaveChangesAsync(ct);

        return (tableCount, rowCount, file.ChoiceSets.Count);
    }
}
