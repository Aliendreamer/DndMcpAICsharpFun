namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public interface IEntityExtractionLlmClient
{
    Task<ExtractionResponse> ExtractAsync(ExtractionRequest request, CancellationToken ct);
}
