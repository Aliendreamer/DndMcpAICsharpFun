namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public interface IPdfTextExtractor
{
    IEnumerable<(int PageNumber, string Text)> ExtractPages(string filePath);
}
