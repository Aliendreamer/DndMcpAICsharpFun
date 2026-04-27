namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface ILlmClassifier
{
    Task<IReadOnlyList<string>> ClassifyPageAsync(string pageText, CancellationToken ct = default);
}
