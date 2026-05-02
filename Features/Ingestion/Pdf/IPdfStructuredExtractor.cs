using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public interface IPdfStructuredExtractor
{
    IEnumerable<StructuredPage> ExtractPages(string filePath);
    StructuredPage? ExtractSinglePage(string filePath, int pageNumber);
}
