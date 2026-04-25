namespace DndMcpAICsharpFun.Features.Ingestion;

public interface IIngestionOrchestrator
{
    Task IngestBookAsync(int recordId, CancellationToken cancellationToken = default);
}
