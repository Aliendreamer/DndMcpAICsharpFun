namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public interface IPdfBlockExtractor
{
    Task<PdfExtraction> ExtractBlocksAsync(string filePath, CancellationToken ct = default);
}