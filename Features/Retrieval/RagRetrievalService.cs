using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Features.Retrieval;

public sealed class RagRetrievalService(
    IQdrantSearchClient qdrant,
    IEmbeddingService embedding,
    IOptions<QdrantOptions> qdrantOptions,
    IOptions<RetrievalOptions> retrievalOptions) : IRagRetrievalService
{
    private readonly string _collectionName = qdrantOptions.Value.BlocksCollectionName;
    private readonly RetrievalOptions _options = retrievalOptions.Value;

    public async Task<IList<RetrievalResult>> SearchAsync(RetrievalQuery query, CancellationToken ct = default)
    {
        var points = await ExecuteSearchAsync(query, ct);
        return points
            .Select(static p => new RetrievalResult(
                QdrantPayloadMapper.GetText(p.Payload),
                QdrantPayloadMapper.ToChunkMetadata(p.Payload),
                p.Score))
            .ToList();
    }

    public async Task<IList<RetrievalDiagnosticResult>> SearchDiagnosticAsync(RetrievalQuery query, CancellationToken ct = default)
    {
        var points = await ExecuteSearchAsync(query, ct);
        return points
            .Select(static p => new RetrievalDiagnosticResult(
                QdrantPayloadMapper.GetText(p.Payload),
                QdrantPayloadMapper.ToChunkMetadata(p.Payload),
                p.Score,
                p.Id.Uuid))
            .ToList();
    }

    private async Task<IReadOnlyList<ScoredPoint>> ExecuteSearchAsync(RetrievalQuery query, CancellationToken ct)
    {
        var vectors = await embedding.EmbedAsync([query.QueryText], ct);
        var vector = vectors[0].AsMemory();
        var filter = BuildFilter(query);
        var limit = (ulong)Math.Min(query.TopK, _options.MaxTopK);

        return await qdrant.SearchAsync(
            _collectionName,
            vector,
            filter: filter,
            limit: limit,
            scoreThreshold: _options.ScoreThreshold,
            cancellationToken: ct);
    }

    private static Filter? BuildFilter(RetrievalQuery query)
    {
        var conditions = new List<Condition>();

        if (query.Version.HasValue)
            conditions.Add(KeywordCondition(QdrantPayloadFields.Version, query.Version.Value.ToString()));

        if (query.Category.HasValue)
            conditions.Add(KeywordCondition(QdrantPayloadFields.Category, query.Category.Value.ToString()));

        if (!string.IsNullOrWhiteSpace(query.SourceBook))
            conditions.Add(KeywordCondition(QdrantPayloadFields.SourceBook, query.SourceBook));

        if (!string.IsNullOrWhiteSpace(query.EntityName))
            conditions.Add(KeywordCondition(QdrantPayloadFields.EntityName, query.EntityName));

        if (query.BookType.HasValue)
            conditions.Add(KeywordCondition(QdrantPayloadFields.BookType, query.BookType.Value.ToString()));

        if (conditions.Count == 0)
            return null;

        var filter = new Filter();
        foreach (var c in conditions)
            filter.Must.Add(c);
        return filter;
    }

    private static Condition KeywordCondition(string key, string value) =>
        new()
        {
            Field = new FieldCondition
            {
                Key = key,
                Match = new Match { Keyword = value }
            }
        };
}
