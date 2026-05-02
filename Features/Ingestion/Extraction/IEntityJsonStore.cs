using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface IEntityJsonStore
{
    Task SavePageAsync(int bookId, StructuredPage page, IReadOnlyList<ExtractedEntity> entities, CancellationToken ct = default);
    Task<IReadOnlyList<PageData>> LoadAllPagesAsync(int bookId, CancellationToken ct = default);
    Task RunMergePassAsync(int bookId, CancellationToken ct = default);
    IEnumerable<string> ListPageFiles(int bookId);
    void DeleteAllPages(int bookId);
}
