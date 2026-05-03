using UglyToad.PdfPig.Core;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed partial class DoclingBlockExtractor(
    IDoclingPdfConverter converter,
    ILogger<DoclingBlockExtractor> logger) : IPdfBlockExtractor
{
    public IEnumerable<PdfBlock> ExtractBlocks(string filePath)
    {
        // Docling exposes only an async API; ExtractBlocks is sync to match the
        // existing interface. Running inside BackgroundService → no
        // SynchronizationContext, so blocking on the task is safe.
        var doc = converter.ConvertAsync(filePath, CancellationToken.None)
            .GetAwaiter().GetResult();

        LogConverted(logger, Path.GetFileName(filePath), doc.Items.Count);

        var perPageOrder = new Dictionary<int, int>();
        foreach (var item in doc.Items)
        {
            var text = item.Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            var order = perPageOrder.TryGetValue(item.PageNumber, out var n) ? n : 0;
            perPageOrder[item.PageNumber] = order + 1;

            yield return new PdfBlock(text, item.PageNumber, order, default(PdfRectangle));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Docling produced {ItemCount} items for {FileName}")]
    private static partial void LogConverted(ILogger logger, string fileName, int itemCount);
}
