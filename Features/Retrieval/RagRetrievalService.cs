using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Infrastructure.Search;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Features.Retrieval;

public sealed class RagRetrievalService(
    IQdrantSearchClient qdrant,
    IEmbeddingService embedding,
    IOptions<QdrantOptions> qdrantOptions,
    IOptions<RetrievalOptions> retrievalOptions,
    QdrantSparseState sparseState,
    RerankingService rerankingService,
    IOptions<RerankerOptions> rerankerOptions) : IRagRetrievalService
{
    private readonly string _collectionName = qdrantOptions.Value.BlocksCollectionName;
    private readonly RetrievalOptions _options = retrievalOptions.Value;
    private readonly RerankerOptions _rerankerOpts = rerankerOptions.Value;

    public async Task<IList<RetrievalResult>> SearchAsync(RetrievalQuery query, CancellationToken ct = default)
    {
        var candidates = await FetchCandidatesAsync(query, ct);
        return await ApplyRerankerAsync(query, candidates, ct);
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

    private async Task<IList<RetrievalResult>> FetchCandidatesAsync(RetrievalQuery query, CancellationToken ct)
    {
        bool shouldRerank = _rerankerOpts.Enabled && _rerankerOpts.RerankBlocks;
        var topK = shouldRerank
            ? (ulong)Math.Min(_options.MaxTopK, _rerankerOpts.CandidatePoolSize)
            : (ulong)Math.Min(query.TopK, _options.MaxTopK);

        var points = await ExecuteSearchAsync(query, ct, topK);
        return points
            .Select(static p => new RetrievalResult(
                QdrantPayloadMapper.GetText(p.Payload),
                QdrantPayloadMapper.ToChunkMetadata(p.Payload),
                p.Score))
            .ToList();
    }

    private async Task<IList<RetrievalResult>> ApplyRerankerAsync(
        RetrievalQuery query, IList<RetrievalResult> candidates, CancellationToken ct)
    {
        if (!_rerankerOpts.Enabled || !_rerankerOpts.RerankBlocks)
            return candidates.Take(query.TopK).ToList();

        var reranked = await rerankingService.RerankAsync(
            query.QueryText, (IReadOnlyList<RetrievalResult>)candidates,
            static r => r.Text, query.TopK, ct);
        return reranked.ToList();
    }

    private async Task<IReadOnlyList<ScoredPoint>> ExecuteSearchAsync(
        RetrievalQuery query, CancellationToken ct, ulong? overrideLimit = null)
    {
        var vectors = await embedding.EmbedAsync([query.QueryText], ct);
        var vector = vectors[0].AsMemory();
        var filter = BuildFilter(query);
        var limit = overrideLimit ?? (ulong)Math.Min(query.TopK, _options.MaxTopK);

        if (sparseState.SparseSupported)
        {
            var sparse = Bm25Vectorizer.ComputeBatch([query.QueryText])[0];
            return await qdrant.QueryAsync(
                _collectionName,
                vector,
                sparse,
                filter: filter,
                limit: limit,
                cancellationToken: ct);
        }

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
