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
            Walk(root, result, parentTitle: null);
        return result;
    }

    private static void Walk(BookmarkNode node, List<PdfBookmark> result, string? parentTitle)
    {
        string? selfTitle = null;
        if (node is DocumentBookmarkNode doc && IsMeaningfulTitle(doc.Title))
        {
            selfTitle = doc.Title;
            result.Add(new PdfBookmark(doc.Title, doc.PageNumber, parentTitle));
        }

        // Children inherit the nearest meaningful ancestor's title — this lets
        // MM's "Aboleth" / "Beholder" leaves under a parent "Monsters (A-Z)"
        // bookmark inherit Monster category via BookmarkTocMapper fallback.
        var contextForChildren = selfTitle ?? parentTitle;
        foreach (var child in node.Children)
            Walk(child, result, contextForChildren);
    }

    private static bool IsMeaningfulTitle(string title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length < 3) return false;
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No embedded bookmarks found in {FileName} — falling back to all-categories extraction")]
    private static partial void LogNoBookmarks(ILogger logger, string fileName);
}
