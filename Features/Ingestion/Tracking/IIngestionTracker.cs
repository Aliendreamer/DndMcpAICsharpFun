using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Features.Ingestion.Tracking;

public interface IIngestionTracker
{
    Task<IngestionRecord?> GetByHashAsync(string hash, CancellationToken ct = default);
    Task<IngestionRecord> CreateAsync(IngestionRecord record, CancellationToken ct = default);
    Task MarkProcessingAsync(int id, CancellationToken ct = default);
    Task MarkCompletedAsync(int id, int chunkCount, CancellationToken ct = default);
    Task MarkFailedAsync(int id, string error, CancellationToken ct = default);
    Task ResetForReingestionAsync(int id, CancellationToken ct = default);
    Task<IList<IngestionRecord>> GetPendingAndFailedAsync(CancellationToken ct = default);
    Task<IList<IngestionRecord>> GetAllAsync(CancellationToken ct = default);
    Task<IngestionRecord?> GetByIdAsync(int id, CancellationToken ct = default);
}
