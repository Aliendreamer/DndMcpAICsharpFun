using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
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
    IOptions<RerankerOptions> rerankerOptions,
    SpellClassIndex spellClassIndex) : IEntityRetrievalService
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
            h.Envelope.SettingTags, Truncate(h.Envelope.CanonicalText, 240), h.Score,
            Authority: h.Envelope.Authority
        )).ToList();
    }

    private const int DefaultSetCap = 50;
    private const int MaxSetCap = 200;

    // Upper bound on the spell set scanned for a castable-by-class join (spells are ~700 corpus-wide).
    private const int SpellClassMaxScan = 3000;

    public async Task<EntitySetResult> ListAsync(EntitySearchQuery q, int cap, CancellationToken ct)
    {
        var clamped = Math.Clamp(cap <= 0 ? DefaultSetCap : cap, 1, MaxSetCap);

        // spell-class-join: a castable-by-class query needs a scroll-all-then-filter — the class
        // relationship isn't a payload field, so Qdrant can't filter it; total must be the
        // class-filtered count, not the raw spell count.
        if (!string.IsNullOrWhiteSpace(q.CastableByClass))
            return await ListSpellsByClassAsync(q, clamped, ct);

        // race-ability-filter: same query-time scroll-then-filter shape as CastableByClass —
        // the boosted-ability data isn't a payload field, so Qdrant can't filter it.
        if (!string.IsNullOrWhiteSpace(q.AbilityBonus))
            return await ListRacesByAbilityAsync(q, clamped, ct);

        var (total, hits) = await store.ListByFilterAsync(BuildFilters(q), clamped, ct);
        var rows = hits.Select(h => ToRow(h.Envelope)).ToList();
        return new EntitySetResult(total, rows.Count, rows);
    }

    private async Task<EntitySetResult> ListSpellsByClassAsync(EntitySearchQuery q, int cap, CancellationToken ct)
    {
        var spellFilters = BuildFilters(q) with { Type = EntityType.Spell };
        var (_, hits) = await store.ListByFilterAsync(spellFilters, SpellClassMaxScan, ct);
        var matched = hits
            .Where(h => spellClassIndex.CanCast(q.CastableByClass!, h.Envelope.Name, h.Envelope.SourceBook))
            .ToList();
        var rows = matched.Take(cap).Select(h => ToRow(h.Envelope)).ToList();
        return new EntitySetResult(matched.Count, rows.Count, rows);
    }


    // Upper bound on the race set scanned for an ability-bonus join (races are a small corpus slice).
    private const int RaceAbilityMaxScan = 3000;

    private async Task<EntitySetResult> ListRacesByAbilityAsync(EntitySearchQuery q, int cap, CancellationToken ct)
    {
        var code = q.AbilityBonus!.Trim().ToLowerInvariant();
        var raceFilters = BuildFilters(q) with { Type = EntityType.Race };
        var (_, hits) = await store.ListByFilterAsync(raceFilters, RaceAbilityMaxScan, ct);
        var matched = hits
            .Where(h => RaceAbilityParser.BoostedAbilities(h.Envelope.Fields).Contains(code))
            .ToList();
        var rows = matched.Take(cap).Select(h => ToRow(h.Envelope)).ToList();
        return new EntitySetResult(matched.Count, rows.Count, rows);
    }

    private static EntityFilters BuildFilters(EntitySearchQuery q) => new(
        Type: q.Type, SourceBook: q.SourceBook, Edition: q.Edition,
        BookType: q.BookType, SettingTag: q.SettingTag, Keyword: q.Keyword,
        CrNumericLte: q.CrNumericLte, CrNumericGte: q.CrNumericGte,
        SpellLevel: q.SpellLevel, DamageType: q.DamageType,
        Srd: q.Srd, Srd52: q.Srd52, BasicRules2024: q.BasicRules2024);

    // Best-effort discriminators pulled from the entity's fields (present per type: cr/level/damageType).
    private static EntitySetRow ToRow(EntityEnvelope e)
    {
        string? cr = null;
        int? level = null;
        string? dmg = null;
        if (e.Fields.ValueKind == JsonValueKind.Object)
        {
            if (e.Fields.TryGetProperty("cr", out var crEl))
                cr = crEl.ValueKind == JsonValueKind.String ? crEl.GetString() : crEl.ToString();
            if (e.Fields.TryGetProperty("level", out var lvlEl)
                && lvlEl.ValueKind == JsonValueKind.Number && lvlEl.TryGetInt32(out var lv))
                level = lv;
            if (e.Fields.TryGetProperty("damageType", out var dEl) && dEl.ValueKind == JsonValueKind.String)
                dmg = dEl.GetString();
        }
        return new EntitySetRow(e.Id, e.Type, e.Name, e.SourceBook, e.Page, cr, level, dmg);
    }

    public async Task<IList<EntityDiagnosticResult>> SearchDiagnosticAsync(EntitySearchQuery q, CancellationToken ct)
    {
        var hits = await ExecuteAsync(q, ct);
        return hits.Select(h => new EntityDiagnosticResult(
            h.Envelope.Id, h.Envelope.Type, h.Envelope.Name,
            h.Envelope.SourceBook, h.Envelope.Edition, h.Envelope.Page,
            h.Envelope.SettingTags, h.PointId, h.Envelope.Fields, h.Score,
            Authority: h.Envelope.Authority
        )).ToList();
    }

    private async Task<IList<EntitySearchHit>> ExecuteAsync(EntitySearchQuery q, CancellationToken ct)
    {
        var topK = Math.Min(q.TopK <= 0 ? 10 : q.TopK, _retrieval.MaxTopK);
        var vectors = await embeddings.EmbedAsync(new[] { q.QueryText }, ct);
        var vector = vectors[0];
        var filters = BuildFilters(q);

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