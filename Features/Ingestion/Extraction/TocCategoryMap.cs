using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed class TocCategoryMap
{
    private readonly TocSectionEntry[] _entries;

    public TocCategoryMap(IEnumerable<TocSectionEntry> entries)
    {
        var sorted = entries.OrderBy(static e => e.StartPage).ToArray();
        _entries = new TocSectionEntry[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            var endPage = sorted[i].EndPage
                ?? (i + 1 < sorted.Length ? sorted[i + 1].StartPage - 1 : int.MaxValue);
            _entries[i] = sorted[i] with { EndPage = endPage };
        }
    }

    public bool IsEmpty => _entries.Length == 0;

    public ContentCategory? GetCategory(int pageNumber) => GetEntry(pageNumber)?.Category;

    public TocSectionEntry? GetEntry(int pageNumber)
    {
        foreach (var entry in _entries)
        {
            if (entry.StartPage <= pageNumber && pageNumber <= entry.EndPage!.Value)
                return entry;
        }
        return null;
    }
}
