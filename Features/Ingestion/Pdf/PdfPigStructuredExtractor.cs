using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed partial class PdfPigStructuredExtractor(
    IOptions<IngestionOptions> options,
    ILogger<PdfPigStructuredExtractor> logger) : IPdfStructuredExtractor
{
    private readonly int _minPageCharacters = options.Value.MinPageCharacters;

    // Group lines within this many PDF units into the same block.
    private const double BlockGapThreshold = 12.0;

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

        // Group words into lines by their Y (bottom) position.
        var lines = words
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom, 0))
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var lineWords = g.OrderBy(w => w.BoundingBox.Left).ToList();
                var letters = lineWords.SelectMany(w => w.Letters).ToList();
                var sizes = letters.Select(l => l.FontSize).Order().ToList();
                var median = sizes.Count > 0 ? sizes[sizes.Count / 2] : 0.0;
                var text = string.Join(" ", lineWords.Select(w => w.Text));
                var y = g.Key;
                return (Y: y, FontSize: median, Text: text);
            })
            .ToList();

        // Aggregate consecutive lines that are close together into paragraph blocks.
        var blockData = AggregateIntoBlocks(lines);

        var blocks = InferHeadingLevels(blockData);
        var rawText = string.Join("\n", blocks.Select(static b => b.Text));

        if (rawText.Length < _minPageCharacters)
            LogSparsePage(logger, page.Number, rawText.Length);

        return new StructuredPage(page.Number, rawText, blocks);
    }

    private static IReadOnlyList<(double FontSize, string Text)> AggregateIntoBlocks(
        IReadOnlyList<(double Y, double FontSize, string Text)> lines)
    {
        if (lines.Count == 0) return [];

        var result = new List<(double FontSize, string Text)>();
        var currentLines = new List<(double Y, double FontSize, string Text)> { lines[0] };

        for (var i = 1; i < lines.Count; i++)
        {
            var prev = currentLines[^1];
            var curr = lines[i];
            var gap = prev.Y - curr.Y;

            if (gap <= BlockGapThreshold)
            {
                currentLines.Add(curr);
            }
            else
            {
                result.Add(CollapseBlock(currentLines));
                currentLines = [curr];
            }
        }

        result.Add(CollapseBlock(currentLines));
        return result;
    }

    private static (double FontSize, string Text) CollapseBlock(
        IReadOnlyList<(double Y, double FontSize, string Text)> lines)
    {
        var sizes = lines.Select(static l => l.FontSize).Order().ToList();
        var median = sizes.Count > 0 ? sizes[sizes.Count / 2] : 0.0;
        var text = string.Join(" ", lines.Select(static l => l.Text));
        return (median, text);
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

        // The smallest (body-text) size is always "body".
        // Heading levels are assigned only to the largest N-1 distinct sizes (capped at 3 heading levels).
        var bodySize = distinctSizes[^1];

        string GetLevel(double size) => size <= bodySize
            ? "body"
            : distinctSizes.Count switch
            {
                <= 1 => "body",
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
