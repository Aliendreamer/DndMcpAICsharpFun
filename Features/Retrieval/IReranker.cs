namespace DndMcpAICsharpFun.Features.Retrieval;

public interface IReranker
{
    bool Enabled { get; }
    Task<float[]> RerankAsync(string query, IReadOnlyList<string> passages, CancellationToken ct);
}