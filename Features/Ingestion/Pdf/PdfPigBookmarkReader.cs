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
        Flatten(bookmarks.Roots, result);
        return result;
    }

    private static void Flatten(IReadOnlyList<BookmarkNode> nodes, List<PdfBookmark> result)
    {
        foreach (var node in nodes)
        {
            if (node is DocumentBookmarkNode docNode)
                result.Add(new PdfBookmark(docNode.Title, docNode.PageNumber));

            if (node.Children.Count > 0)
                Flatten(node.Children, result);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No embedded bookmarks found in {FileName} — falling back to all-categories extraction")]
    private static partial void LogNoBookmarks(ILogger logger, string fileName);
}
