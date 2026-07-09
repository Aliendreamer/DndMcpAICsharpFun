namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public interface IPdfStructureConverter
{
    Task<PdfStructureDocument> ConvertAsync(string filePath, CancellationToken ct = default);
}