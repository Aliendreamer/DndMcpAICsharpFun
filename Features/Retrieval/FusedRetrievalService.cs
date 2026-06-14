using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Features.Retrieval;

/// <summary>
/// Fused cross-channel retrieval.
/// Embeds the query once, fetches candidate pools from dnd_blocks (prose) and
/// dnd_entities (structured entities), merges them into <see cref="FusedCandidate"/>s,
/// reranks the combined list via <see cref="RerankingService"/>, and returns the
/// merged top-K. Each result carries a <c>source</c> tag ("prose" | "entity").
/// </summary>
public sealed class FusedRetrievalService(
    IEmbeddingService embedding,
    IQdrantSearchClient qdrant,
    IEntityVectorStore entityStore,
    RerankingService rerankingService,
    IOptions<QdrantOptions> qdrantOptions,
    IOptions<RetrievalOptions> retrievalOptions,
    IOptions<RerankerOptions> rerankerOptions,
    QdrantSparseState sparseState) : IFusedRetrievalService
{
    private readonly QdrantOptions _qdrant = qdrantOptions.Value;
    private readonly RetrievalOptions _retrieval = retrievalOptions.Value;
    private readonly RerankerOptions _reranker = rerankerOptions.Value;

    public async Task<IReadOnlyList<FusedCandidate>> SearchAsync(
        string query, int topK, CancellationToken ct = default)
    {
        var poolSize = Math.Min(_reranker.CandidatePoolSize, _retrieval.MaxTopK);
        topK = Math.Min(topK <= 0 ? 5 : topK, _retrieval.MaxTopK);

        // Embed once and reuse for both channels
        var vectors = await embedding.EmbedAsync([query], ct);
        var vector = vectors[0];

        // Fetch prose candidates (dnd_blocks)
        var proseTask = FetchProseAsync(vector, (ulong)poolSize, ct, query);
        // Fetch entity candidates (dnd_entities)
        var entityTask = FetchEntitiesAsync(vector, poolSize, ct);

        await Task.WhenAll(proseTask, entityTask);

        var prose = proseTask.Result;
        var entities = entityTask.Result;

        // Merge into unified list
        var all = new List<FusedCandidate>(prose.Count + entities.Count);
        all.AddRange(prose);
        all.AddRange(entities);

        if (all.Count == 0)
            return [];

        // Rerank the union
        var reranked = await rerankingService.RerankAsync(
            query,
            (IReadOnlyList<FusedCandidate>)all,
            static c => c.Text,
            topK,
            ct);

        return reranked;
    }

    private async Task<List<FusedCandidate>> FetchProseAsync(
        float[] vector, ulong limit, CancellationToken ct, string queryText = "")
    {
        IReadOnlyList<ScoredPoint> points;

        if (sparseState.SparseSupported)
        {
            var sparse = Bm25Vectorizer.ComputeBatch([queryText])[0];
            points = await qdrant.QueryAsync(
                _qdrant.BlocksCollectionName, vector.AsMemory(), sparse,
                limit: limit, cancellationToken: ct);
        }
        else
        {
            points = await qdrant.SearchAsync(
                _qdrant.BlocksCollectionName, vector.AsMemory(),
                limit: limit,
                scoreThreshold: _retrieval.ScoreThreshold,
                cancellationToken: ct);
        }

        return points.Select(p =>
        {
            var meta = QdrantPayloadMapper.ToChunkMetadata(p.Payload);
            var title = meta.SectionTitle ?? meta.Chapter;
            return new FusedCandidate(
                Source: "prose",
                Id: p.Id.Uuid,
                Title: title,
                Text: QdrantPayloadMapper.GetText(p.Payload),
                Score: p.Score);
        }).ToList();
    }

    private async Task<List<FusedCandidate>> FetchEntitiesAsync(
        float[] vector, int limit, CancellationToken ct)
    {
        var hits = await entityStore.SearchAsync(vector, new EntityFilters(), limit, ct);
        return hits.Select(h => new FusedCandidate(
            Source: "entity",
            Id: h.Envelope.Id,
            Title: h.Envelope.Name,
            Text: h.Envelope.CanonicalText,
            Score: h.Score)).ToList();
    }
}
