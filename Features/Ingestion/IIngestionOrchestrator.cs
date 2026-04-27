namespace DndMcpAICsharpFun.Features.Ingestion;

public interface IIngestionOrchestrator
{
    Task IngestBookAsync(int recordId, CancellationToken cancellationToken = default);
    Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken cancellationToken = default);
}
