using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public static class BookmarkTocMapper
{
    public static IReadOnlyList<TocSectionEntry> Map(IReadOnlyList<PdfBookmark> bookmarks)
    {
        if (bookmarks.Count == 0) return [];

        var entries = new List<TocSectionEntry>(bookmarks.Count);
        foreach (var b in bookmarks)
        {
            var category = HeadingCategoryClassifier.Guess(b.Title);
            // Fall back to the parent bookmark's category when the leaf title
            // matches no keyword (e.g. monster names under "Monsters (A-Z)").
            if (category == ContentCategory.Rule && !string.IsNullOrEmpty(b.ParentTitle))
            {
                var parentCategory = HeadingCategoryClassifier.Guess(b.ParentTitle);
                if (parentCategory != ContentCategory.Rule)
                    category = parentCategory;
            }
            entries.Add(new TocSectionEntry(b.Title, category, b.PageNumber));
        }
        return entries;
    }
}