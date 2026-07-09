using DndMcpAICsharpFun.Features.Ingestion;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class BlockSplitterTests
{
    [Fact]
    public void SplitLongBlock_ShortText_ReturnsSingleChunk()
    {
        var text = new string('a', 500);
        var result = BlockIngestionOrchestrator.SplitLongBlock(text).ToList();
        Assert.Single(result);
        Assert.Equal(text, result[0]);
    }

    [Fact]
    public void SplitLongBlock_AtThreshold_ReturnsSingleChunk()
    {
        var text = new string('a', 800);
        var result = BlockIngestionOrchestrator.SplitLongBlock(text).ToList();
        Assert.Single(result);
    }

    [Fact]
    public void SplitLongBlock_LongText_ReturnsMultipleChunks()
    {
        var text = new string('a', 3000);
        var result = BlockIngestionOrchestrator.SplitLongBlock(text).ToList();
        Assert.True(result.Count >= 2, $"expected ≥2 chunks, got {result.Count}");
    }

    [Fact]
    public void SplitLongBlock_AllChunksUnderMaxChars()
    {
        var text = new string('a', 5000);
        var result = BlockIngestionOrchestrator.SplitLongBlock(text).ToList();
        Assert.All(result, chunk => Assert.True(chunk.Length <= 800,
            $"chunk length {chunk.Length} exceeded 800"));
    }

    [Fact]
    public void SplitLongBlock_PrefersWordBoundary()
    {
        // 100 'a' words separated by spaces — total ~10100 chars
        var words = string.Join(" ", Enumerable.Repeat(new string('a', 100), 100));
        var result = BlockIngestionOrchestrator.SplitLongBlock(words).ToList();

        // No chunk should end mid-word (every chunk except the last should
        // end with no partial trailing word)
        for (var i = 0; i < result.Count - 1; i++)
        {
            var chunk = result[i];
            // Either the chunk ends right after a word boundary, or it's clean text
            // Validate by checking the chunk doesn't end mid-'aaaa…' block
            Assert.False(chunk.EndsWith("a") && !chunk.EndsWith(new string('a', 100)),
                $"chunk {i} appears to end mid-word: …{chunk[Math.Max(0, chunk.Length - 20)..]}");
        }
    }

    [Fact]
    public void SplitLongBlock_OverlapsConsecutiveChunks()
    {
        // Build text with predictable markers
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < 200; i++) sb.Append($"sentence{i:D3} word ");
        var text = sb.ToString();   // ~3400 chars

        var result = BlockIngestionOrchestrator.SplitLongBlock(text).ToList();
        Assert.True(result.Count >= 2);

        // Last 100 chars of chunk N should overlap with first 100 chars of chunk N+1
        // (allow some tolerance because of word-boundary breaks)
        for (var i = 0; i < result.Count - 1; i++)
        {
            var prevTail = result[i][^Math.Min(150, result[i].Length)..];
            var nextHead = result[i + 1][..Math.Min(150, result[i + 1].Length)];
            // At least one shared word should appear in both
            var prevWords = prevTail.Split(' ').Where(w => w.StartsWith("sentence")).ToHashSet();
            var nextWords = nextHead.Split(' ').Where(w => w.StartsWith("sentence")).ToHashSet();
            Assert.True(prevWords.Overlaps(nextWords),
                $"no shared sentence between chunks {i} and {i + 1}");
        }
    }

    [Fact]
    public void SplitLongBlock_EmptyText_ReturnsSingleChunk()
    {
        var result = BlockIngestionOrchestrator.SplitLongBlock(string.Empty).ToList();
        Assert.Single(result);
        Assert.Equal(string.Empty, result[0]);
    }
}