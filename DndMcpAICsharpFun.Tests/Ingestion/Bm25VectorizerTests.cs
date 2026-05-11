using DndMcpAICsharpFun.Features.Ingestion;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class Bm25VectorizerTests
{
    [Fact]
    public void ComputeBatch_OutputLengthMatchesInput()
    {
        var result = Bm25Vectorizer.ComputeBatch(["hello world", "test sentence", "another doc"]);
        Assert.Equal(3, result.Length);
    }

    [Fact]
    public void ComputeBatch_IndicesSorted()
    {
        var result = Bm25Vectorizer.ComputeBatch(["hello world test sentence"]);
        var indices = result[0].Indices;
        for (var i = 0; i < indices.Length - 1; i++)
            Assert.True(indices[i] < indices[i + 1], $"Index at {i} ({indices[i]}) >= index at {i + 1} ({indices[i + 1]})");
    }

    [Fact]
    public void ComputeBatch_ValuesPositive()
    {
        var result = Bm25Vectorizer.ComputeBatch(["hello world"]);
        Assert.All(result[0].Values, v => Assert.True(v > 0f));
    }

    [Fact]
    public void ComputeBatch_IndicesInRange()
    {
        var result = Bm25Vectorizer.ComputeBatch(["hello world this is a test sentence for bm25"]);
        Assert.All(result[0].Indices, idx => Assert.InRange(idx, 0, 29999));
    }

    [Fact]
    public void ComputeBatch_RareTermScoresHigherThanCommonTerm()
    {
        // "rare" appears in 1 of 3 docs, "common" appears in all 3
        var texts = new[] { "common word rare term", "common only", "common again" };
        var result = Bm25Vectorizer.ComputeBatch(texts);

        var doc0 = result[0];
        var rareIdx = Math.Abs("rare".GetHashCode()) % 30000;
        var commonIdx = Math.Abs("common".GetHashCode()) % 30000;

        float rareScore = 0f, commonScore = 0f;
        for (var i = 0; i < doc0.Indices.Length; i++)
        {
            if (doc0.Indices[i] == rareIdx) rareScore = doc0.Values[i];
            if (doc0.Indices[i] == commonIdx) commonScore = doc0.Values[i];
        }

        Assert.True(rareScore > 0f, "rare term should have positive score");
        Assert.True(commonScore > 0f, "common term should have positive score");
        Assert.True(rareScore > commonScore,
            $"rare ({rareScore}) should score higher than common ({commonScore})");
    }

    [Fact]
    public void ComputeBatch_EmptyText_ReturnsEmptySparseVector()
    {
        var result = Bm25Vectorizer.ComputeBatch([""]);
        Assert.Empty(result[0].Indices);
        Assert.Empty(result[0].Values);
    }

    [Fact]
    public void Tokenize_SplitsOnNonLetters()
    {
        var tokens = Bm25Vectorizer.Tokenize("hello, world! 123 test");
        Assert.Equal(["hello", "world", "test"], tokens);
    }

    [Fact]
    public void Tokenize_Lowercases()
    {
        var tokens = Bm25Vectorizer.Tokenize("Hello WORLD");
        Assert.Equal(["hello", "world"], tokens);
    }
}
