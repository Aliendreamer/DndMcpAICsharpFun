using System.Text.Json;
using System.Text.RegularExpressions;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public sealed partial class EntityRetrievalService(
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
        return hits.Select((h, i) =>
        {
            var e = h.Hit.Envelope;
            return new EntitySearchResult(
                e.Id, e.Type, e.Name, e.SourceBook, e.Edition, e.Page, e.SettingTags,
                i >= WideResults
                    ? Truncate(e.CanonicalText, SnippetChars)
                    : EntityTextPieces.Matching(e.CanonicalText, e.Notes, h.Hit.PieceVectors, h.Question,
                        h.Named || i == 0 ? NamedChars : WideChars, h.Best),
                h.Hit.Score,
                Authority: e.Authority);
        }).ToList();
    }

    // The first results carry enough text to answer from: the chat model almost never asks for the rest of a cut
    // text, and fills the gap from memory. Bounded, because two searches share the chat's 8k context.
    private const int WideResults = 3;
    private const int NamedChars = 3000;
    private const int WideChars = EntityTextPieces.MaxChars;
    private const int SnippetChars = 240;

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

        // race-damage-defense-filter: same query-time scroll-then-filter shape — resist/immune
        // damage-type data isn't a payload field, so Qdrant can't filter it.
        if (!string.IsNullOrWhiteSpace(q.ResistDamage) || !string.IsNullOrWhiteSpace(q.ImmuneDamage))
            return await ListRacesByDamageDefenseAsync(q, clamped, ct);

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

    private const int RaceDamageDefenseMaxScan = 3000;

    private async Task<EntitySetResult> ListRacesByDamageDefenseAsync(EntitySearchQuery q, int cap, CancellationToken ct)
    {
        var resist = string.IsNullOrWhiteSpace(q.ResistDamage) ? null : q.ResistDamage!.Trim().ToLowerInvariant();
        var immune = string.IsNullOrWhiteSpace(q.ImmuneDamage) ? null : q.ImmuneDamage!.Trim().ToLowerInvariant();
        var raceFilters = BuildFilters(q) with { Type = EntityType.Race };
        var (_, hits) = await store.ListByFilterAsync(raceFilters, RaceDamageDefenseMaxScan, ct);
        var matched = hits
            .Where(h =>
                (resist is null || RaceDamageDefenseParser.DamageTypes(h.Envelope.Fields, "resist").Contains(resist))
                && (immune is null || RaceDamageDefenseParser.DamageTypes(h.Envelope.Fields, "immune").Contains(immune)))
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
        return hits.Select(x => x.Hit).Select(h => new EntityDiagnosticResult(
            h.Envelope.Id, h.Envelope.Type, h.Envelope.Name,
            h.Envelope.SourceBook, h.Envelope.Edition, h.Envelope.Page,
            h.Envelope.SettingTags, h.PointId, h.Envelope.Fields, h.Score,
            Authority: h.Envelope.Authority
        )).ToList();
    }

    private async Task<IReadOnlyList<RankedHit>> ExecuteAsync(EntitySearchQuery q, CancellationToken ct)
    {
        var topK = Math.Min(q.TopK <= 0 ? 10 : q.TopK, _retrieval.MaxTopK);
        var vectors = await embeddings.EmbedAsync(new[] { q.QueryText }, ct);
        var vector = vectors[0];
        var filters = BuildFilters(q);

        IReadOnlyList<(EntitySearchHit Hit, TextPiece Best)> ranked;
        bool shouldRerank = _rerankerOpts.Enabled && _rerankerOpts.RerankEntities;
        var poolSize = shouldRerank ? Math.Min(_rerankerOpts.CandidatePoolSize, _retrieval.MaxTopK) : topK;
        var pool = (IReadOnlyList<EntitySearchHit>)await store.SearchAsync(vector, filters, poolSize, ct);
        // The model's type is a guess ("Devour Intellect" as a Spell): other types join the pool, and stay
        // only when the query names them or one of their features.
        HashSet<string>? ofType = null;
        if (filters.Type is not null)
        {
            ofType = pool.Select(h => h.Envelope.Id).ToHashSet(StringComparer.Ordinal);
            var anyType = await store.SearchAsync(vector, filters with { Type = null }, poolSize, ct);
            pool = [.. pool, .. anyType.Where(h => !ofType.Contains(h.Envelope.Id))];
        }
        if (shouldRerank)
        {
            // The whole pool is ranked: a named entity may stand below topK.
            ranked = await rerankingService.RerankByPieceAsync(
                q.QueryText, pool, h => h.Envelope.CanonicalText, pool.Count, ct);
        }
        else
        {
            ranked = pool.Select(h => (h, EntityTextPieces.Split(h.Envelope.CanonicalText)[0])).ToList();
        }

        return ranked
            .Select(r =>
            {
                // The entity's own name outranks one of its features ("Devour Intellect" names the Intellect Devourer).
                var rank = IsNamedIn(q.QueryText, r.Hit.Envelope) ? 1000 + r.Hit.Envelope.Name.Length
                    : NamedFeatureLength(q.QueryText, r.Hit.Envelope);
                return new RankedHit(r.Hit, r.Best, rank > 0, rank, vector);
            })
            .Where(r => ofType is null || r.Named || ofType.Contains(r.Hit.Envelope.Id))
            .OrderByDescending(r => r.NameRank)
            .Take(topK)
            .ToList();
    }

    private sealed record RankedHit(EntitySearchHit Hit, TextPiece Best, bool Named, int NameRank, float[] Question);

    // A feature line of a stat block or class: "Devour Intellect. The intellect devourer targets…"
    [GeneratedRegex(@"^([A-Z][\p{L}'’-]*(?: [\p{L}'’-]+){1,4})(?: \([^)\n]*\))?\. ", RegexOptions.Multiline)]
    private static partial Regex FeatureLine();

    /// <summary>The length of the longest feature of at least two words that the query names, else 0.</summary>
    private static int NamedFeatureLength(string query, EntityEnvelope entity)
    {
        if (entity.Type is EntityType.Rule or EntityType.Lore) return 0;
        var text = query.Replace('’', '\'');
        return FeatureLine().Matches(entity.CanonicalText)
            .Select(m => m.Groups[1].Value.Replace('’', '\''))
            .Where(f => Regex.IsMatch(text, $@"(?<!\w){Regex.Escape(f)}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .Select(f => f.Length)
            .DefaultIfEmpty(0)
            .Max();
    }

    /// <summary>
    /// Whether the query names the entity, as whole words. Questions hold words that are also names of rules and lore
    /// sections ("attack", "hit points"): those count only when the query is nothing else.
    /// </summary>
    private static bool IsNamedIn(string query, EntityEnvelope entity)
    {
        var name = entity.Name.Trim().Replace('’', '\'');
        if (name.Length == 0) return false;
        var text = query.Trim().Replace('’', '\'');
        if (text.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        return entity.Type is not (EntityType.Rule or EntityType.Lore)
            && Regex.IsMatch(text, $@"(?<!\w){Regex.Escape(name)}(?:'?s|es)?(?!\w)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";
}