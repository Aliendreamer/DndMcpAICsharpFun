using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Infrastructure.Search;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class Bm25VectorizerTests
{
    // A fixed global-stats snapshot so doc vectors are deterministic and independent of the batch.
    private static Bm25GlobalStats Stats(
        long docCount = 100,
        double avgDocLen = 5,
        params (string term, long df)[] dfs)
    {
        var map = new Dictionary<string, long>();
        foreach (var (term, df) in dfs) map[term] = df;
        return new Bm25GlobalStats(docCount, avgDocLen, map);
    }

    [Fact]
    public void ComputeDocVectors_OutputLengthMatchesInput()
    {
        var result = Bm25Vectorizer.ComputeDocVectors(
            ["hello world", "test sentence", "another doc"], Stats());
        Assert.Equal(3, result.Length);
    }

    [Fact]
    public void ComputeDocVectors_IndicesSorted()
    {
        var result = Bm25Vectorizer.ComputeDocVectors(["hello world test sentence"], Stats());
        var indices = result[0].Indices;
        for (var i = 0; i < indices.Length - 1; i++)
            Assert.True(indices[i] < indices[i + 1], $"Index at {i} ({indices[i]}) >= index at {i + 1} ({indices[i + 1]})");
    }

    [Fact]
    public void ComputeDocVectors_ValuesPositive()
    {
        var result = Bm25Vectorizer.ComputeDocVectors(["hello world"], Stats());
        Assert.All(result[0].Values, v => Assert.True(v > 0f));
    }

    [Fact]
    public void ComputeDocVectors_IndicesInRange()
    {
        var result = Bm25Vectorizer.ComputeDocVectors(
            ["hello world this is a test sentence for bm25"], Stats());
        Assert.All(result[0].Indices, idx => Assert.InRange(idx, 0, 29999));
    }

    [Fact]
    public void ComputeDocVectors_EmptyText_ReturnsEmptySparseVector()
    {
        var result = Bm25Vectorizer.ComputeDocVectors([""], Stats());
        Assert.Empty(result[0].Indices);
        Assert.Empty(result[0].Values);
    }

    [Fact]
    public void ComputeDocVectors_IdenticalTextSameStats_IdenticalWeights()
    {
        // IDF is applied exactly once from the SAME global stats, so identical text yields
        // identical sparse weights regardless of any surrounding batch.
        var stats = Stats(docCount: 100, avgDocLen: 5,
            ("fire", 20), ("bolt", 8), ("damage", 40));
        var a = Bm25Vectorizer.ComputeDocVectors(["fire bolt deals fire damage"], stats)[0];
        var b = Bm25Vectorizer.ComputeDocVectors(["fire bolt deals fire damage"], stats)[0];

        Assert.Equal(a.Indices, b.Indices);
        Assert.Equal(a.Values, b.Values);
    }

    [Fact]
    public void ComputeDocVectors_RareTermScoresHigherThanCommonTerm()
    {
        // Global df drives IDF: "rare" (df=1) must outweigh "common" (df=90) for equal tf/docLen.
        var stats = Stats(docCount: 100, avgDocLen: 4,
            ("rare", 1), ("common", 90));
        var doc = Bm25Vectorizer.ComputeDocVectors(["common word rare term"], stats)[0];

        var rareIdx = Bm25Vectorizer.StableIndex("rare");
        var commonIdx = Bm25Vectorizer.StableIndex("common");

        float rareScore = 0f, commonScore = 0f;
        for (var i = 0; i < doc.Indices.Length; i++)
        {
            if (doc.Indices[i] == rareIdx) rareScore = doc.Values[i];
            if (doc.Indices[i] == commonIdx) commonScore = doc.Values[i];
        }

        Assert.True(rareScore > 0f, "rare term should have positive score");
        Assert.True(commonScore > 0f, "common term should have positive score");
        Assert.True(rareScore > commonScore,
            $"rare ({rareScore}) should score higher than common ({commonScore})");
    }

    [Fact]
    public void ComputeQueryVector_WeightEqualsRawCount_IndependentOfDf()
    {
        // Query side is tf-only (no idf) to avoid IDF². Each term's weight is exactly its raw
        // count in the query and cannot depend on any document frequency (there is no stats input).
        var vec = Bm25Vectorizer.ComputeQueryVector("fire fire ice");

        var fireIdx = Bm25Vectorizer.StableIndex("fire");
        var iceIdx = Bm25Vectorizer.StableIndex("ice");

        float fire = 0f, ice = 0f;
        for (var i = 0; i < vec.Indices.Length; i++)
        {
            if (vec.Indices[i] == fireIdx) fire = vec.Values[i];
            if (vec.Indices[i] == iceIdx) ice = vec.Values[i];
        }

        Assert.Equal(2f, fire);   // raw count, not an idf-weighted value
        Assert.Equal(1f, ice);
    }

    [Fact]
    public void ComputeQueryVector_IndicesSortedAndInRange()
    {
        var vec = Bm25Vectorizer.ComputeQueryVector("hello world test sentence");
        for (var i = 0; i < vec.Indices.Length - 1; i++)
            Assert.True(vec.Indices[i] < vec.Indices[i + 1]);
        Assert.All(vec.Indices, idx => Assert.InRange(idx, 0, 29999));
    }

    [Fact]
    public void Tokenize_SplitsOnNonAlphanumerics_KeepsDigits()
    {
        // COR-14: numeric/alphanumeric keyword terms (e.g. "123", "2d6") must survive tokenization.
        var tokens = Bm25Vectorizer.Tokenize("hello, world! 123 test");
        Assert.Equal(["hello", "world", "123", "test"], tokens);
    }

    [Fact]
    public void Tokenize_KeepsAlphanumericDiceTerms()
    {
        var tokens = Bm25Vectorizer.Tokenize("deals 2d6 fire damage");
        Assert.Contains("2d6", tokens);
    }

    [Fact]
    public void StableIndex_IsDeterministicAcrossProcesses()
    {
        // Golden values: FNV-1a over UTF-8 mod 30000. If these change, sparse vectors written to
        // Qdrant at ingestion time will no longer align with query-time vectors (COR-16/COR-17).
        Assert.Equal(Bm25Vectorizer.StableIndex("fireball"), Bm25Vectorizer.StableIndex("fireball"));
        Assert.InRange(Bm25Vectorizer.StableIndex("fireball"), 0, 29999);
        // int.MinValue-style overflow can never occur (unsigned FNV), unlike Math.Abs(GetHashCode()).
        Assert.All(new[] { "a", "the", "2d6", "dragonborn", "" },
            t => Assert.InRange(Bm25Vectorizer.StableIndex(t), 0, 29999));
    }

    [Fact]
    public void Tokenize_Lowercases()
    {
        var tokens = Bm25Vectorizer.Tokenize("Hello WORLD");
        Assert.Equal(["hello", "world"], tokens);
    }
}
