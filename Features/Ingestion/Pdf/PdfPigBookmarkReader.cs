using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed partial class PdfPigBookmarkReader(
    ILogger<PdfPigBookmarkReader> logger) : IPdfBookmarkReader
{
    public IReadOnlyList<PdfBookmark> ReadBookmarks(string filePath)
    {
        using var document = PdfDocument.Open(filePath);

        if (!document.TryGetBookmarks(out var bookmarks))
        {
            LogNoBookmarks(logger, Path.GetFileName(filePath));
            return [];
        }

        var result = new List<PdfBookmark>();
        foreach (var root in bookmarks.Roots)
        {
            if (root is DocumentBookmarkNode rootDoc)
                result.Add(new PdfBookmark(rootDoc.Title, rootDoc.PageNumber));

            foreach (var child in root.Children)
            {
                if (child is DocumentBookmarkNode childDoc)
                    result.Add(new PdfBookmark(childDoc.Title, childDoc.PageNumber));
            }
        }
        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No embedded bookmarks found in {FileName} — falling back to all-categories extraction")]
    private static partial void LogNoBookmarks(ILogger logger, string fileName);
}
