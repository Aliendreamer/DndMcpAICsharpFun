using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface ILlmEntityExtractor
{
    Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(
        string pageText,
        string entityType,
        int pageNumber,
        string sourceBook,
        string version,
        string entityName,
        int sectionStartPage,
        int sectionEndPage,
        CancellationToken ct = default);
}
