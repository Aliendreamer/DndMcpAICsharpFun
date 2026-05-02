namespace DndMcpAICsharpFun.Features.Ingestion;

public interface IBookDeletionService
{
    Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken cancellationToken = default);
}
