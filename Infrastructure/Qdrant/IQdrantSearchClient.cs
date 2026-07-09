using Qdrant.Client.Grpc;

using DomainSparseVector = DndMcpAICsharpFun.Infrastructure.Search.SparseVector;

namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public interface IQdrantSearchClient
{
    Task<IReadOnlyList<ScoredPoint>> SearchAsync(
        string collectionName,
        ReadOnlyMemory<float> vector,
        Filter? filter = null,
        ulong limit = 10,
        float? scoreThreshold = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScoredPoint>> QueryAsync(
        string collectionName,
        ReadOnlyMemory<float> denseVector,
        DomainSparseVector sparseVector,
        Filter? filter = null,
        ulong limit = 10,
        CancellationToken cancellationToken = default);
}