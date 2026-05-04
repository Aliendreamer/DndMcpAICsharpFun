namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public interface IEntityExtractionOrchestrator
{
    Task ExtractAsync(int bookId, bool force, CancellationToken ct);
}
