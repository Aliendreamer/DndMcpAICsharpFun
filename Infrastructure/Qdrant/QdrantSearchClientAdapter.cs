using System.Diagnostics.CodeAnalysis;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using DomainSparseVector = DndMcpAICsharpFun.Infrastructure.Search.SparseVector;

namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

[ExcludeFromCodeCoverage]
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

    public Task<IReadOnlyList<ScoredPoint>> QueryAsync(
        string collectionName,
        ReadOnlyMemory<float> denseVector,
        DomainSparseVector sparseVector,
        Filter? filter = null,
        ulong limit = 10,
        CancellationToken cancellationToken = default)
    {
        var sparseUintIndices = Array.ConvertAll(sparseVector.Indices, static i => (uint)i);

        var densePrefetch = new PrefetchQuery
        {
            Query = denseVector.Span.ToArray(),
            Filter = filter,
            Limit = limit
        };

        var sparsePrefetch = new PrefetchQuery
        {
            Query = (sparseVector.Values, sparseUintIndices),
            Using = "text-sparse",
            Filter = filter,
            Limit = limit
        };

        return client.QueryAsync(
            collectionName,
            query: Fusion.Rrf,
            prefetch: [densePrefetch, sparsePrefetch],
            limit: limit,
            cancellationToken: cancellationToken);
    }
}
