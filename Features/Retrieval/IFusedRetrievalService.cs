namespace DndMcpAICsharpFun.Features.Retrieval;

public interface IFusedRetrievalService
{
    Task<IReadOnlyList<FusedCandidate>> SearchAsync(string query, int topK, CancellationToken ct = default);
}
