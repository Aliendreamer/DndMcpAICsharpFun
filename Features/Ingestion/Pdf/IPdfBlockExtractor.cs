namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public interface IPdfBlockExtractor
{
    IEnumerable<PdfBlock> ExtractBlocks(string filePath);
}
