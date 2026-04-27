using DndMcpAICsharpFun.Infrastructure.Sqlite;

using Microsoft.Extensions.Options;

using UglyToad.PdfPig;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed partial class PdfPigTextExtractor(
    IOptions<IngestionOptions> options,
    ILogger<PdfPigTextExtractor> logger) : IPdfTextExtractor
{
    private readonly int _minPageCharacters = options.Value.MinPageCharacters;

    public IEnumerable<(int PageNumber, string Text)> ExtractPages(string filePath)
    {
        using var document = PdfDocument.Open(filePath);

        foreach (var page in document.GetPages())
        {
            // Group words by their Y position to reconstruct line structure.
            // GetWords() uses spatial proximity for proper word spacing, and we
            // then sort lines top-to-bottom (descending Y in PDF coordinates).
            var lines = page.GetWords()
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom, 0))
                .OrderByDescending(g => g.Key)
                .Select(g => string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));

            var text = string.Join("\n", lines);

            if (text.Length < _minPageCharacters)
                LogSparsePage(logger, Path.GetFileName(filePath), page.Number, text.Length);

            yield return (page.Number, text);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sparse page detected in {File} at page {Page} ({Chars} chars)")]
    private static partial void LogSparsePage(ILogger logger, string file, int page, int chars);
}
