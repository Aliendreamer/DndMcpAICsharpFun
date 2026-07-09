using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public sealed class EntityRetrievalService(
    IEmbeddingService embeddings,
    IEntityVectorStore store,
    IOptions<RetrievalOptions> retrievalOptions,
    RerankingService rerankingService,
    IOptions<RerankerOptions> rerankerOptions) : IEntityRetrievalService
{
    private readonly RetrievalOptions _retrieval = retrievalOptions.Value;
    private readonly RerankerOptions _rerankerOpts = rerankerOptions.Value;

    public async Task<EntityFullResult?> GetByIdAsync(string id, CancellationToken ct)
    {
        var envelope = await store.GetByIdAsync(id, ct);
        return envelope is null ? null : new EntityFullResult(envelope);
    }

    public async Task<IList<EntitySearchResult>> SearchAsync(EntitySearchQuery q, CancellationToken ct)
    {
        var hits = await ExecuteAsync(q, ct);
        return hits.Select(h => new EntitySearchResult(
            h.Envelope.Id, h.Envelope.Type, h.Envelope.Name,
            h.Envelope.SourceBook, h.Envelope.Edition, h.Envelope.Page,
            h.Envelope.SettingTags, Truncate(h.Envelope.CanonicalText, 240), h.Score
        )).ToList();
    }

    public async Task<IList<EntityDiagnosticResult>> SearchDiagnosticAsync(EntitySearchQuery q, CancellationToken ct)
    {
        var hits = await ExecuteAsync(q, ct);
        return hits.Select(h => new EntityDiagnosticResult(
            h.Envelope.Id, h.Envelope.Type, h.Envelope.Name,
            h.Envelope.SourceBook, h.Envelope.Edition, h.Envelope.Page,
            h.Envelope.SettingTags, h.PointId, h.Envelope.Fields, h.Score
        )).ToList();
    }

    private async Task<IList<EntitySearchHit>> ExecuteAsync(EntitySearchQuery q, CancellationToken ct)
    {
        var topK = Math.Min(q.TopK <= 0 ? 10 : q.TopK, _retrieval.MaxTopK);
        var vectors = await embeddings.EmbedAsync(new[] { q.QueryText }, ct);
        var vector = vectors[0];
        var filters = new EntityFilters(
            Type: q.Type, SourceBook: q.SourceBook, Edition: q.Edition,
            BookType: q.BookType, SettingTag: q.SettingTag, Keyword: q.Keyword,
            CrNumericLte: q.CrNumericLte, CrNumericGte: q.CrNumericGte,
            SpellLevel: q.SpellLevel, DamageType: q.DamageType,
            Srd: q.Srd, Srd52: q.Srd52, BasicRules2024: q.BasicRules2024);

        bool shouldRerank = _rerankerOpts.Enabled && _rerankerOpts.RerankEntities;
        if (shouldRerank)
        {
            var poolSize = Math.Min(_rerankerOpts.CandidatePoolSize, _retrieval.MaxTopK);
            var pool = (IReadOnlyList<EntitySearchHit>)await store.SearchAsync(vector, filters, poolSize, ct);
            var reranked = await rerankingService.RerankAsync<EntitySearchHit>(
                q.QueryText, pool, h => h.Envelope.CanonicalText, topK, ct);
            return reranked.ToList();
        }

        var vectorResults = await store.SearchAsync(vector, filters, topK, ct);
        return vectorResults.Take(topK).ToList();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";
}