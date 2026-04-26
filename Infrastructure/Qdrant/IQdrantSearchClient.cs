using Qdrant.Client.Grpc;

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
}
