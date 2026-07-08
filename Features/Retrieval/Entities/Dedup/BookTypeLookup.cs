using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

public interface IBookTypeLookup
{
    Task<IReadOnlyDictionary<string, BookType>> BuildAsync(CancellationToken ct = default);
}

public sealed class BookTypeLookup(IIngestionTracker tracker) : IBookTypeLookup
{
    public async Task<IReadOnlyDictionary<string, BookType>> BuildAsync(CancellationToken ct = default)
    {
        var all = await tracker.GetAllAsync(limit: int.MaxValue, offset: 0, ct);
        return Build(all);
    }

    public static IReadOnlyDictionary<string, BookType> Build(IEnumerable<IngestionRecord> records)
    {
        var map = new Dictionary<string, BookType>(StringComparer.Ordinal);
        foreach (var r in records)
        {
            if (!string.IsNullOrEmpty(r.FivetoolsSourceKey))
                map[r.FivetoolsSourceKey] = r.BookType;

            if (!string.IsNullOrEmpty(r.DisplayName))
                map[r.DisplayName] = r.BookType;
        }
        return map;
    }
}
