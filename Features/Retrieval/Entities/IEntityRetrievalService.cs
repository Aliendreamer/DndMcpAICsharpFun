namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public interface IEntityRetrievalService
{
    Task<EntityFullResult?> GetByIdAsync(string id, CancellationToken ct);
    Task<IList<EntitySearchResult>> SearchAsync(EntitySearchQuery query, CancellationToken ct);

    /// <summary>Complete filter-set retrieval (entity-set-query): compact rows + honest total.</summary>
    Task<EntitySetResult> ListAsync(EntitySearchQuery query, int cap, CancellationToken ct);
    Task<IList<EntityDiagnosticResult>> SearchDiagnosticAsync(EntitySearchQuery query, CancellationToken ct);
}