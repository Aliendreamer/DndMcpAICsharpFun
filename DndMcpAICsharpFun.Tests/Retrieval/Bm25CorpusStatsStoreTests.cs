using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using DndMcpAICsharpFun.Tests.Persistence;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval;

[Collection("postgres")]
public sealed class Bm25CorpusStatsStoreTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly Bm25CorpusStatsStore _store = new(new TestDb(pg));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ApplyBook_Twice_IsSelfCorrecting_NotDoubled()
    {
        var df = new Dictionary<string, int> { ["fire"] = 3, ["ice"] = 1 };

        await _store.ApplyBookAsync("hashA", df, documentCount: 5, totalTokenLength: 200);

        var afterFirst = await _store.ReadAsync();
        afterFirst.DocFrequencies["fire"].Should().Be(3);
        afterFirst.DocFrequencies["ice"].Should().Be(1);
        afterFirst.DocumentCount.Should().Be(5);

        // Re-apply the SAME book with the SAME values.
        await _store.ApplyBookAsync("hashA", df, documentCount: 5, totalTokenLength: 200);

        var afterSecond = await _store.ReadAsync();
        afterSecond.DocFrequencies["fire"].Should().Be(3, "re-applying the same book must not double-count");
        afterSecond.DocFrequencies["ice"].Should().Be(1);
        afterSecond.DocumentCount.Should().Be(5);
        afterSecond.AvgDocLength.Should().Be(200.0 / 5);
    }

    [Fact]
    public async Task TwoBooks_AddUp_AndRemoveReverts()
    {
        await _store.ApplyBookAsync("hashA",
            new Dictionary<string, int> { ["fire"] = 3, ["ice"] = 1 }, 5, 200);
        await _store.ApplyBookAsync("hashB",
            new Dictionary<string, int> { ["fire"] = 2, ["water"] = 4 }, 3, 120);

        var combined = await _store.ReadAsync();
        combined.DocFrequencies["fire"].Should().Be(5);
        combined.DocFrequencies["ice"].Should().Be(1);
        combined.DocFrequencies["water"].Should().Be(4);
        combined.DocumentCount.Should().Be(8);
        combined.AvgDocLength.Should().Be(320.0 / 8);

        await _store.RemoveBookAsync("hashB");

        var afterRemove = await _store.ReadAsync();
        afterRemove.DocFrequencies["fire"].Should().Be(3);
        afterRemove.DocFrequencies["ice"].Should().Be(1);
        afterRemove.DocFrequencies.Should().NotContainKey("water", "hashB's only term must be gone");
        afterRemove.DocumentCount.Should().Be(5);
        afterRemove.AvgDocLength.Should().Be(200.0 / 5);
    }

    [Fact]
    public async Task RemoveBook_Absent_IsNoOp()
    {
        await _store.ApplyBookAsync("hashA",
            new Dictionary<string, int> { ["fire"] = 3 }, 5, 200);

        await _store.RemoveBookAsync("does-not-exist");

        var stats = await _store.ReadAsync();
        stats.DocFrequencies["fire"].Should().Be(3);
        stats.DocumentCount.Should().Be(5);
    }

    [Fact]
    public async Task ReIngest_ChangedContent_ReflectsOnlyNew()
    {
        await _store.ApplyBookAsync("hashA",
            new Dictionary<string, int> { ["fire"] = 3 }, 5, 200);

        await _store.ApplyBookAsync("hashA",
            new Dictionary<string, int> { ["fire"] = 1, ["water"] = 2 }, 2, 90);

        var stats = await _store.ReadAsync();
        stats.DocFrequencies["fire"].Should().Be(1, "old fire=3 must be fully removed");
        stats.DocFrequencies["water"].Should().Be(2);
        stats.DocumentCount.Should().Be(2);
        stats.AvgDocLength.Should().Be(90.0 / 2);
    }

    [Fact]
    public async Task Rebuild_RederivesGlobals_FromPerBookRows()
    {
        await _store.ApplyBookAsync("hashA",
            new Dictionary<string, int> { ["fire"] = 3, ["ice"] = 1 }, 5, 200);
        await _store.ApplyBookAsync("hashB",
            new Dictionary<string, int> { ["fire"] = 2, ["water"] = 4 }, 3, 120);

        // Corrupt the derived global aggregates directly (simulating drift from an interrupted ingest).
        await using (var db = pg.NewContext())
        {
            var fire = await db.Bm25TermStats.FirstAsync(t => t.Term == "fire");
            fire.DocumentFrequency = 999;
            var corpus = await db.Bm25CorpusStats.FirstAsync(c => c.Id == 1);
            corpus.DocumentCount = 42;
            corpus.TotalTokenLength = 9999;
            await db.SaveChangesAsync();
        }

        await _store.RebuildAsync();

        var rebuilt = await _store.ReadAsync();
        rebuilt.DocFrequencies["fire"].Should().Be(5);
        rebuilt.DocFrequencies["ice"].Should().Be(1);
        rebuilt.DocFrequencies["water"].Should().Be(4);
        rebuilt.DocumentCount.Should().Be(8);
        rebuilt.AvgDocLength.Should().Be(320.0 / 8);

        // Globals must equal the exact sum of the per-book rows.
        await using var check = pg.NewContext();
        var books = await check.Bm25BookStats.AsNoTracking().ToListAsync();
        books.Sum(b => b.DocumentCount).Should().Be(rebuilt.DocumentCount);
    }

    [Fact]
    public async Task AvgDocLength_EmptyStore_IsOne()
    {
        var empty = await _store.ReadAsync();
        empty.DocumentCount.Should().Be(0);
        empty.AvgDocLength.Should().Be(1.0, "no divide-by-zero on an empty corpus");
        empty.DocFrequencies.Should().BeEmpty();
    }

    // Regression for the production-only bug where the transactional store methods threw
    // "NpgsqlRetryingExecutionStrategy does not support user-initiated transactions": the app's context is
    // registered with EnableRetryOnFailure, but the default test context is not, so 856 tests stayed green
    // while ingestion failed live. This exercises Apply/Remove/Rebuild against a retry-ENABLED factory.
    [Fact]
    public async Task Transactional_ops_succeed_under_the_retrying_execution_strategy()
    {
        var store = new Bm25CorpusStatsStore(new RetryingTestDb(pg));
        var df = new Dictionary<string, int> { ["fire"] = 2, ["ice"] = 1 };

        await store.ApplyBookAsync("hashR", df, documentCount: 3, totalTokenLength: 90);
        (await store.ReadAsync()).DocumentCount.Should().Be(3);

        await store.RebuildAsync();                    // ExecuteDelete + tracked Adds inside a transaction
        (await store.ReadAsync()).DocFrequencies["fire"].Should().Be(2, "rebuild re-derives from the per-book row");

        await store.RemoveBookAsync("hashR");
        (await store.ReadAsync()).DocumentCount.Should().Be(0);
    }


    // Task 4.1 (audit P3): the term-stat lookup batches into chunks of ~500 terms (`WHERE Term IN (...)`)
    // instead of one round trip per term. None of the tests above exercise more than a handful of terms,
    // so this pins correctness across a chunk boundary (750 terms > the 500-term chunk size) for both the
    // apply and remove paths.
    [Fact]
    public async Task ApplyAndRemove_WithMoreTermsThanOneChunk_AppliesAllTermsCorrectly()
    {
        const int termCount = 750;
        var df = Enumerable.Range(0, termCount).ToDictionary(i => $"term-{i}", _ => 1);

        await _store.ApplyBookAsync("hashChunked", df, documentCount: termCount, totalTokenLength: termCount * 10);

        var afterApply = await _store.ReadAsync();
        afterApply.DocFrequencies.Should().HaveCount(termCount);
        afterApply.DocFrequencies["term-0"].Should().Be(1);
        afterApply.DocFrequencies["term-499"].Should().Be(1, "the last term in the first 500-term chunk");
        afterApply.DocFrequencies["term-500"].Should().Be(1, "the first term in the second chunk");
        afterApply.DocFrequencies["term-749"].Should().Be(1, "the last term overall");

        await _store.RemoveBookAsync("hashChunked");

        var afterRemove = await _store.ReadAsync();
        afterRemove.DocFrequencies.Should().BeEmpty("every term's df drops to 0 and the row is pruned");
    }

    /// <summary>A context factory that mirrors production's <c>EnableRetryOnFailure</c> configuration,
    /// so transactional store methods are exercised under the retrying execution strategy.</summary>
    private sealed class RetryingTestDb(PostgresFixture pg) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(pg.Container.GetConnectionString(), o => o.EnableRetryOnFailure())
                .Options);
    }
}