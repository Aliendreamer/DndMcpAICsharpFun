namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface IJsonIngestionPipeline
{
    Task IngestAsync(int bookId, string fileHash, CancellationToken ct = default);
}
