namespace DndMcpAICsharpFun.Features.Retrieval;

public interface IRagRetrievalService
{
    Task<IList<RetrievalResult>> SearchAsync(RetrievalQuery query, CancellationToken ct = default);
    Task<IList<RetrievalDiagnosticResult>> SearchDiagnosticAsync(RetrievalQuery query, CancellationToken ct = default);
}
