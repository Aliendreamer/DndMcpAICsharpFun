using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Retrieval;
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
}
