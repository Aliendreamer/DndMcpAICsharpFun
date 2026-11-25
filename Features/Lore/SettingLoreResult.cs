using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Features.Lore;

/// <param name="Page">The printed page the passage starts on; null when it is not known.</param>
public sealed record CitedPassage(string Text, string SourceBook, string? Section, double Score, int? Page = null)
{
    public static CitedPassage From(RetrievalResult r) => new(
        r.Text, r.Metadata.SourceBook, r.Metadata.SectionTitle ?? r.Metadata.Chapter, r.Score,
        BookCatalog.PrintedPage(r.Metadata.SourceBook, r.Metadata.PageNumber));
}

public sealed record SettingLoreResult(
    string? Setting,
    IReadOnlyList<string> ScopedBooks,
    IReadOnlyList<CitedPassage> Passages);
