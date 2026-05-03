namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public interface IDoclingPdfConverter
{
    Task<DoclingDocument> ConvertAsync(string filePath, CancellationToken ct = default);
}
