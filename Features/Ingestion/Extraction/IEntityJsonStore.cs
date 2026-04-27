using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface IEntityJsonStore
{
    Task SavePageAsync(int bookId, int pageNumber, IReadOnlyList<ExtractedEntity> entities, CancellationToken ct = default);
    Task<IReadOnlyList<IReadOnlyList<ExtractedEntity>>> LoadAllPagesAsync(int bookId, CancellationToken ct = default);
    Task RunMergePassAsync(int bookId, CancellationToken ct = default);
    IEnumerable<string> ListPageFiles(int bookId);
    void DeleteAllPages(int bookId);
}
