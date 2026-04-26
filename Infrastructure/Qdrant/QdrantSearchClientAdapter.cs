using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public sealed class QdrantSearchClientAdapter(QdrantClient client) : IQdrantSearchClient
{
    public Task<IReadOnlyList<ScoredPoint>> SearchAsync(
        string collectionName,
        ReadOnlyMemory<float> vector,
        Filter? filter = null,
        ulong limit = 10,
        float? scoreThreshold = null,
        CancellationToken cancellationToken = default)
        => client.SearchAsync(collectionName, vector, filter: filter, limit: limit,
            scoreThreshold: scoreThreshold, cancellationToken: cancellationToken);
}
