using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed partial class PdfPigStructuredExtractor(
    IOptions<IngestionOptions> options,
    ILogger<PdfPigStructuredExtractor> logger) : IPdfStructuredExtractor
{
    private readonly int _minPageCharacters = options.Value.MinPageCharacters;

    public IEnumerable<StructuredPage> ExtractPages(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
            yield return ExtractPage(page);
    }

    public StructuredPage? ExtractSinglePage(string filePath, int pageNumber)
    {
        using var document = PdfDocument.Open(filePath);
        var page = document.GetPages().FirstOrDefault(p => p.Number == pageNumber);
        if (page is null) return null;
        return ExtractPage(page);
    }

    private StructuredPage ExtractPage(Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0)
        {
            if (0 < _minPageCharacters)
                LogSparsePage(logger, page.Number, 0);
            return new StructuredPage(page.Number, string.Empty, []);
        }

        var textBlocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        var orderedBlocks = UnsupervisedReadingOrderDetector.Instance.Get(textBlocks).ToList();

        var blockData = orderedBlocks
            .Select(static b =>
            {
                var letters = b.TextLines.SelectMany(static l => l.Words).SelectMany(static w => w.Letters).ToList();
                var sizes = letters.Select(static l => l.FontSize).Order().ToList();
                var median = sizes.Count > 0 ? sizes[sizes.Count / 2] : 0.0;
                return (FontSize: median, Text: b.Text);
            })
            .ToArray();

        var blocks = InferHeadingLevels(blockData);
        var rawText = string.Join("\n", blocks.Select(static b => b.Text));

        if (rawText.Length < _minPageCharacters)
            LogSparsePage(logger, page.Number, rawText.Length);

        return new StructuredPage(page.Number, rawText, blocks);
    }

    public static IReadOnlyList<PageBlock> InferHeadingLevels(
        IReadOnlyList<(double FontSize, string Text)> blockData)
    {
        if (blockData.Count == 0) return [];

        var distinctSizes = blockData
            .Select(static b => b.FontSize)
            .Where(static s => s > 0)
            .Distinct()
            .OrderDescending()
            .ToList();

        var bodySize = distinctSizes.Count > 0 ? distinctSizes[^1] : 0.0;

        string GetLevel(double size) => size <= bodySize || distinctSizes.Count <= 1
            ? "body"
            : distinctSizes.Count switch
            {
                _ when size >= distinctSizes[0] => "h1",
                _ when distinctSizes.Count > 1 && size >= distinctSizes[1] => "h2",
                _ when distinctSizes.Count > 2 && size >= distinctSizes[2] => "h3",
                _ => "body"
            };

        return blockData
            .Select((b, i) => new PageBlock(i + 1, GetLevel(b.FontSize), b.Text))
            .ToList();
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Sparse page {Page} ({Chars} chars)")]
    private static partial void LogSparsePage(ILogger logger, int page, int chars);
}
