namespace DndMcpAICsharpFun.Features.Ingestion;

public interface IIngestionOrchestrator
{
    Task IngestBookAsync(int recordId, CancellationToken cancellationToken = default);
    Task ExtractBookAsync(int recordId, CancellationToken cancellationToken = default);
    Task IngestJsonAsync(int recordId, CancellationToken cancellationToken = default);
    Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken cancellationToken = default);
}
