using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Features.Retrieval;

/// <summary>
/// A snapshot of the corpus-global BM25 statistics: how many documents make up the corpus,
/// the mean document length, and each term's global document frequency.
/// </summary>
public sealed record Bm25GlobalStats(
    long DocumentCount,
    double AvgDocLength,
    IReadOnlyDictionary<string, long> DocFrequencies);

/// <summary>
/// Persisted, self-correcting corpus-statistics store for BM25 (COR-15 Stage A).
/// Global aggregates are the exact sum of per-book contributions, so re-ingesting or
/// deleting a book never double-counts or drifts.
/// </summary>
public interface IBm25CorpusStats
{
    Task<Bm25GlobalStats> ReadAsync(CancellationToken ct = default);

    Task ApplyBookAsync(string fileHash, IReadOnlyDictionary<string, int> termDocFrequencies,
        long documentCount, long totalTokenLength, CancellationToken ct = default);

    Task RemoveBookAsync(string fileHash, CancellationToken ct = default);

    Task RebuildAsync(CancellationToken ct = default);
}

/// <summary>
/// EF Core implementation of <see cref="IBm25CorpusStats"/> over <see cref="AppDbContext"/>.
/// The global <see cref="Bm25TermStat"/> / <see cref="Bm25CorpusStat"/> aggregates are kept in lockstep
/// with the per-book <see cref="Bm25BookStat"/> rows: applying a book first subtracts any prior
/// contribution recorded for its <c>FileHash</c>, then adds the new one (self-correcting).
/// </summary>
public sealed class Bm25CorpusStatsStore(IDbContextFactory<AppDbContext> dbf) : IBm25CorpusStats
{
    private const int CorpusSingletonId = 1;

    public async Task<Bm25GlobalStats> ReadAsync(CancellationToken ct = default)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);

        var frequencies = await db.Bm25TermStats
            .AsNoTracking()
            .ToDictionaryAsync(t => t.Term, t => t.DocumentFrequency, ct);

        var corpus = await db.Bm25CorpusStats
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == CorpusSingletonId, ct);

        var documentCount = corpus?.DocumentCount ?? 0;
        var totalTokenLength = corpus?.TotalTokenLength ?? 0;
        var avgDocLength = documentCount == 0 ? 1.0 : (double)totalTokenLength / documentCount;

        return new Bm25GlobalStats(documentCount, avgDocLength, frequencies);
    }

    public async Task ApplyBookAsync(string fileHash, IReadOnlyDictionary<string, int> termDocFrequencies,
        long documentCount, long totalTokenLength, CancellationToken ct = default)
    {
        // A single SaveChangesAsync at the end is already atomic (EF wraps it in its own transaction),
        // so no explicit user-initiated transaction is needed — which would also be incompatible with the
        // context's retrying execution strategy (EnableRetryOnFailure).
        await using var db = await dbf.CreateDbContextAsync(ct);

        var existing = await db.Bm25BookStats.FirstOrDefaultAsync(b => b.FileHash == fileHash, ct);

        // Compute the NET per-term delta (new contribution minus any prior contribution recorded for this
        // book) and apply it in a single pass. Doing subtract and add as separate tracked operations would
        // mark a term Deleted and then re-resolve the same tracked (Deleted) entity on the add, losing the
        // increment — so a re-ingest must be expressed as one net change per term.
        var termDeltas = new Dictionary<string, long>();
        if (existing is not null)
        {
            foreach (var (term, df) in Deserialize(existing.TermDfJson))
            {
                if (df == 0) continue;
                termDeltas[term] = termDeltas.GetValueOrDefault(term) - df;
            }
        }
        foreach (var (term, df) in termDocFrequencies)
        {
            if (df == 0) continue;
            termDeltas[term] = termDeltas.GetValueOrDefault(term) + df;
        }

        foreach (var (term, delta) in termDeltas)
        {
            if (delta == 0) continue;
            var stat = await db.Bm25TermStats.FirstOrDefaultAsync(t => t.Term == term, ct);
            if (stat is null)
            {
                if (delta > 0)
                {
                    db.Bm25TermStats.Add(new Bm25TermStat { Term = term, DocumentFrequency = delta });
                }
            }
            else
            {
                stat.DocumentFrequency += delta;
                // Never let df go negative; drop the row once no book references the term.
                if (stat.DocumentFrequency <= 0)
                {
                    db.Bm25TermStats.Remove(stat);
                }
            }
        }

        var docDelta = documentCount - (existing?.DocumentCount ?? 0);
        var tokenDelta = totalTokenLength - (existing?.TotalTokenLength ?? 0);
        await AdjustCorpusAsync(db, docDelta, tokenDelta, ct);

        // Upsert the per-book row with the new values.
        var newJson = JsonSerializer.Serialize(termDocFrequencies);
        if (existing is null)
        {
            db.Bm25BookStats.Add(new Bm25BookStat
            {
                FileHash = fileHash,
                DocumentCount = documentCount,
                TotalTokenLength = totalTokenLength,
                TermDfJson = newJson,
            });
        }
        else
        {
            existing.DocumentCount = documentCount;
            existing.TotalTokenLength = totalTokenLength;
            existing.TermDfJson = newJson;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveBookAsync(string fileHash, CancellationToken ct = default)
    {
        // Single atomic SaveChangesAsync — no explicit transaction (see ApplyBookAsync).
        await using var db = await dbf.CreateDbContextAsync(ct);

        var existing = await db.Bm25BookStats.FirstOrDefaultAsync(b => b.FileHash == fileHash, ct);
        if (existing is null) return; // no-op if absent

        var oldTermDf = Deserialize(existing.TermDfJson);
        await SubtractTermsAsync(db, oldTermDf, ct);
        await AdjustCorpusAsync(db, -existing.DocumentCount, -existing.TotalTokenLength, ct);
        db.Bm25BookStats.Remove(existing);

        await db.SaveChangesAsync(ct);
    }

    public async Task RebuildAsync(CancellationToken ct = default)
    {
        // A fresh context per execution-strategy attempt keeps the change tracker clean on retry (this
        // method Adds tracked rows), and the strategy wrap is required because the context is configured
        // with EnableRetryOnFailure, which forbids a raw user-initiated BeginTransactionAsync.
        await using var outer = await dbf.CreateDbContextAsync(ct);
        var strategy = outer.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Clear the derived global aggregates; the per-book rows remain the source of truth.
            await db.Bm25TermStats.ExecuteDeleteAsync(ct);
            await db.Bm25CorpusStats.ExecuteDeleteAsync(ct);

            // Re-sum from every per-book row.
            var termTotals = new Dictionary<string, long>();
            long documentCount = 0;
            long totalTokenLength = 0;

            await foreach (var book in db.Bm25BookStats.AsNoTracking().AsAsyncEnumerable().WithCancellation(ct))
            {
                documentCount += book.DocumentCount;
                totalTokenLength += book.TotalTokenLength;
                foreach (var (term, df) in Deserialize(book.TermDfJson))
                {
                    if (df == 0) continue;
                    termTotals[term] = termTotals.GetValueOrDefault(term) + df;
                }
            }

            foreach (var (term, df) in termTotals)
            {
                if (df <= 0) continue;
                db.Bm25TermStats.Add(new Bm25TermStat { Term = term, DocumentFrequency = df });
            }

            db.Bm25CorpusStats.Add(new Bm25CorpusStat
            {
                Id = CorpusSingletonId,
                DocumentCount = documentCount,
                TotalTokenLength = totalTokenLength,
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    private static async Task SubtractTermsAsync(
        AppDbContext db, IReadOnlyDictionary<string, int> termDf, CancellationToken ct)
    {
        foreach (var (term, df) in termDf)
        {
            if (df == 0) continue;
            var stat = await db.Bm25TermStats.FirstOrDefaultAsync(t => t.Term == term, ct);
            if (stat is null) continue;
            stat.DocumentFrequency -= df;
            // Never let df go negative; drop the row once no book references the term.
            if (stat.DocumentFrequency <= 0)
            {
                db.Bm25TermStats.Remove(stat);
            }
        }
    }

    private static async Task AdjustCorpusAsync(
        AppDbContext db, long docDelta, long tokenDelta, CancellationToken ct)
    {
        var corpus = await db.Bm25CorpusStats.FirstOrDefaultAsync(c => c.Id == CorpusSingletonId, ct);
        if (corpus is null)
        {
            corpus = new Bm25CorpusStat { Id = CorpusSingletonId, DocumentCount = 0, TotalTokenLength = 0 };
            db.Bm25CorpusStats.Add(corpus);
        }

        corpus.DocumentCount = Math.Max(0, corpus.DocumentCount + docDelta);
        corpus.TotalTokenLength = Math.Max(0, corpus.TotalTokenLength + tokenDelta);
    }

    private static IReadOnlyDictionary<string, int> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new Dictionary<string, int>();
}
