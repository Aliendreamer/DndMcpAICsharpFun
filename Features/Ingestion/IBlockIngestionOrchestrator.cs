namespace DndMcpAICsharpFun.Features.Ingestion;

public interface IBlockIngestionOrchestrator
{
    Task IngestBlocksAsync(int recordId, CancellationToken cancellationToken = default);
}
