using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Features.Ingestion.Tracking;

public sealed class SqliteIngestionTracker(IngestionDbContext db) : IIngestionTracker
{
    public Task<IngestionRecord?> GetByHashAsync(string hash, CancellationToken ct = default) =>
        db.IngestionRecords.FirstOrDefaultAsync(r => r.FileHash == hash, ct);

    public Task<IngestionRecord?> GetByIdAsync(int id, CancellationToken ct = default) =>
        db.IngestionRecords.FindAsync([id], ct).AsTask();

    public async Task<IngestionRecord> CreateAsync(IngestionRecord record, CancellationToken ct = default)
    {
        db.IngestionRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return record;
    }

    public async Task MarkHashAsync(int id, string fileHash, CancellationToken ct = default)
    {
        await db.IngestionRecords
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, IngestionStatus.Processing)
                .SetProperty(r => r.FileHash, fileHash), ct);
    }

    public async Task MarkFailedAsync(int id, string error, CancellationToken ct = default)
    {
        await db.IngestionRecords
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, IngestionStatus.Failed)
                .SetProperty(r => r.Error, error), ct);
    }

    public async Task ResetForReingestionAsync(int id, CancellationToken ct = default)
    {
        await db.IngestionRecords
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, IngestionStatus.Pending)
                .SetProperty(r => r.Error, (string?)null)
                .SetProperty(r => r.ChunkCount, (int?)null)
                .SetProperty(r => r.IngestedAt, (DateTime?)null), ct);
    }

    public async Task<IList<IngestionRecord>> GetPendingAndFailedAsync(CancellationToken ct = default) =>
        await db.IngestionRecords
            .Where(r => r.Status == IngestionStatus.Pending || r.Status == IngestionStatus.Failed)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<IList<IngestionRecord>> GetAllAsync(CancellationToken ct = default) =>
        await db.IngestionRecords.OrderBy(r => r.CreatedAt).ToListAsync(ct);

    public async Task MarkDuplicateAsync(int id, CancellationToken ct = default)
    {
        await db.IngestionRecords
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, IngestionStatus.Duplicate), ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var deleted = await db.IngestionRecords
            .Where(r => r.Id == id && r.Status != IngestionStatus.Processing)
            .ExecuteDeleteAsync(ct);
        return deleted > 0;
    }


    public async Task MarkJsonIngestedAsync(int id, int chunkCount, CancellationToken ct = default)
    {
        await db.IngestionRecords
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, IngestionStatus.JsonIngested)
                .SetProperty(r => r.ChunkCount, chunkCount)
                .SetProperty(r => r.IngestedAt, DateTime.UtcNow)
                .SetProperty(r => r.Error, (string?)null), ct);
    }

    public async Task MarkEntitiesIngestedAsync(int id, int entityCount, CancellationToken ct = default)
    {
        await db.IngestionRecords
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, IngestionStatus.EntitiesIngested)
                .SetProperty(r => r.EntityCount, entityCount)
                .SetProperty(r => r.IngestedAt, DateTime.UtcNow)
                .SetProperty(r => r.Error, (string?)null), ct);
    }

    public async Task MarkEntitiesExtractingAsync(int bookId, CancellationToken ct = default)
    {
        await db.IngestionRecords
            .Where(r => r.Id == bookId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, IngestionStatus.EntitiesIngesting)
                .SetProperty(r => r.Error, (string?)null), ct);
    }

    public async Task MarkEntitiesExtractedAsync(int bookId, int entityCount, CancellationToken ct = default)
    {
        // Note: the LLM-extraction phase yields the canonical JSON; downstream
        // ingestion (vector upsert) re-uses MarkEntitiesIngestedAsync. We set
        // the same EntitiesIngested status here because, post-extraction, the
        // structured data exists on disk and the row's EntityCount becomes
        // meaningful. Do NOT overwrite ChunkCount.
        await db.IngestionRecords
            .Where(r => r.Id == bookId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, IngestionStatus.EntitiesIngested)
                .SetProperty(r => r.EntityCount, entityCount)
                .SetProperty(r => r.IngestedAt, DateTime.UtcNow)
                .SetProperty(r => r.Error, (string?)null), ct);
    }

    public async Task MarkEntitiesFailedAsync(int bookId, string error, CancellationToken ct = default)
    {
        await db.IngestionRecords
            .Where(r => r.Id == bookId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, IngestionStatus.EntitiesFailed)
                .SetProperty(r => r.Error, error), ct);
    }
}
