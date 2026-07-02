namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed partial class StructureBlockExtractor(
    IPdfStructureConverter converter,
    ILogger<StructureBlockExtractor> logger) : IPdfBlockExtractor
{
    public async Task<IReadOnlyList<PdfBlock>> ExtractBlocksAsync(string filePath, CancellationToken ct = default)
    {
        var doc = await converter.ConvertAsync(filePath, ct);

        LogConverted(logger, Path.GetFileName(filePath), doc.Items.Count);

        var blocks = new List<PdfBlock>();
        var perPageOrder = new Dictionary<int, int>();
        foreach (var item in doc.Items)
        {
            var text = item.Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            var order = perPageOrder.TryGetValue(item.PageNumber, out var n) ? n : 0;
            perPageOrder[item.PageNumber] = order + 1;

            blocks.Add(new PdfBlock(text, item.PageNumber, order));
        }
        return blocks;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MinerU produced {ItemCount} items for {FileName}")]
    private static partial void LogConverted(ILogger logger, string fileName, int itemCount);
}
