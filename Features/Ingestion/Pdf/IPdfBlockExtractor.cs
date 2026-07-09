namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public interface IPdfBlockExtractor
{
    Task<IReadOnlyList<PdfBlock>> ExtractBlocksAsync(string filePath, CancellationToken ct = default);
}