using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Tracking;

public interface IIngestionTracker
{
    Task<IngestionRecord?> GetByHashAsync(string hash, CancellationToken ct = default);
    Task<IngestionRecord> CreateAsync(IngestionRecord record, CancellationToken ct = default);
    Task MarkHashAsync(int id, string fileHash, CancellationToken ct = default);
    Task MarkFailedAsync(int id, string error, CancellationToken ct = default);
    Task MarkJsonIngestedAsync(int id, int chunkCount, CancellationToken ct = default);
    Task MarkEntitiesIngestedAsync(int id, int entityCount, CancellationToken ct = default);
    Task MarkEntitiesExtractingAsync(int bookId, CancellationToken ct = default);
    Task MarkEntitiesExtractedAsync(int bookId, int entityCount, CancellationToken ct = default);
    Task MarkEntitiesFailedAsync(int bookId, string error, CancellationToken ct = default);
    Task ResetForReingestionAsync(int id, CancellationToken ct = default);
    Task<IList<IngestionRecord>> GetPendingAndFailedAsync(CancellationToken ct = default);
    Task<List<IngestionRecord>> GetAllAsync(int limit = 100, int offset = 0, CancellationToken ct = default);
    Task<IngestionRecord?> GetByIdAsync(int id, CancellationToken ct = default);
    Task MarkDuplicateAsync(int id, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
