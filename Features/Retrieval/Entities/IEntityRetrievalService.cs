namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public interface IEntityRetrievalService
{
    Task<EntityFullResult?> GetByIdAsync(string id, CancellationToken ct);
    Task<IList<EntitySearchResult>> SearchAsync(EntitySearchQuery query, CancellationToken ct);
    Task<IList<EntityDiagnosticResult>> SearchDiagnosticAsync(EntitySearchQuery query, CancellationToken ct);
}