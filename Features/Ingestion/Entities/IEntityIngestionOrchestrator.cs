namespace DndMcpAICsharpFun.Features.Ingestion.Entities;

public interface IEntityIngestionOrchestrator
{
    Task IngestEntitiesAsync(int bookId, CancellationToken ct = default);
}
