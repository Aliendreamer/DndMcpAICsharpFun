using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed class TocCategoryMap
{
    private readonly (int StartPage, ContentCategory? Category)[] _ranges;

    public TocCategoryMap(IEnumerable<(int StartPage, ContentCategory? Category)> ranges)
    {
        _ranges = ranges.OrderBy(static r => r.StartPage).ToArray();
    }

    public bool IsEmpty => _ranges.Length == 0;

    public ContentCategory? GetCategory(int pageNumber)
    {
        ContentCategory? result = null;
        foreach (var (startPage, category) in _ranges)
        {
            if (startPage > pageNumber) break;
            result = category;
        }
        return result;
    }
}
