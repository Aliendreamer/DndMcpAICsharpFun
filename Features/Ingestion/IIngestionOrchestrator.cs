using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion;

public interface IIngestionOrchestrator
{
    Task ExtractBookAsync(int recordId, CancellationToken cancellationToken = default);
    Task IngestJsonAsync(int recordId, CancellationToken cancellationToken = default);
    Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken cancellationToken = default);
    Task<PageData?> ExtractSinglePageAsync(int bookId, int pageNumber, bool save, CancellationToken ct = default);
}
