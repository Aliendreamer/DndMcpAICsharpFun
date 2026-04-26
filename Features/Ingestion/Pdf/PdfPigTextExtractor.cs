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
            var text = page.Text;

            if (text.Length < _minPageCharacters)
                LogSparsePage(logger, Path.GetFileName(filePath), page.Number, text.Length);

            yield return (page.Number, text);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sparse page detected in {File} at page {Page} ({Chars} chars)")]
    private static partial void LogSparsePage(ILogger logger, string file, int page, int chars);
}
