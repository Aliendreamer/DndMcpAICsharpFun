using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed partial class PdfPigBlockExtractor : IPdfBlockExtractor
{
    private readonly IPageSegmenter _segmenter;

    public PdfPigBlockExtractor(IOptions<IngestionOptions> options, ILogger<PdfPigBlockExtractor> logger)
    {
        _segmenter = ResolveSegmenter(options.Value.BlockSegmenter, logger);
    }

    private static IPageSegmenter ResolveSegmenter(string? configured, ILogger logger)
    {
        var value = configured?.Trim() ?? string.Empty;
        if (string.Equals(value, "xycut", StringComparison.OrdinalIgnoreCase))
            return RecursiveXYCut.Instance;
        if (string.Equals(value, "docstrum", StringComparison.OrdinalIgnoreCase) || value.Length == 0)
            return DocstrumBoundingBoxes.Instance;

        LogUnknownSegmenter(logger, value);
        return DocstrumBoundingBoxes.Instance;
    }

    public IEnumerable<PdfBlock> ExtractBlocks(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
            foreach (var block in ExtractFromPage(page, _segmenter))
                yield return block;
    }

    private static IEnumerable<PdfBlock> ExtractFromPage(Page page, IPageSegmenter segmenter)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0) yield break;

        var textBlocks = segmenter.GetBlocks(words);
        var ordered = UnsupervisedReadingOrderDetector.Instance.Get(textBlocks).ToList();

        var order = 0;
        foreach (var block in ordered)
        {
            var text = block.Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            yield return new PdfBlock(text, page.Number, order++, block.BoundingBox);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Unknown Ingestion:BlockSegmenter value '{Value}', falling back to docstrum")]
    private static partial void LogUnknownSegmenter(ILogger logger, string value);
}
