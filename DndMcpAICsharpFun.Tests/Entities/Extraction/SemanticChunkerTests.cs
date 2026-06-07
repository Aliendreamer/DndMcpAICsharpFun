using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class SemanticChunkerTests
{
    private readonly SemanticChunker _chunker = new();

    [Fact]
    public void Split_TextUnderLimit_ReturnsSingleChunkUnchanged()
    {
        var text = "A short paragraph.";
        var chunks = _chunker.Split(text, maxTokensPerChunk: 2000);
        chunks.Should().ContainSingle().Which.Should().Be(text);
    }

    [Fact]
    public void Split_PrefersSubHeadingBoundaries()
    {
        var sectionA = "## Section A\n" + new string('a', 400);
        var sectionB = "## Section B\n" + new string('b', 400);
        var text = sectionA + "\n" + sectionB;
        var chunks = _chunker.Split(text, maxTokensPerChunk: 150);
        chunks.Should().HaveCount(2);
        chunks[0].Should().StartWith("## Section A");
        chunks[1].Should().StartWith("## Section B");
    }

    [Fact]
    public void Split_FallsBackToParagraphBreaks()
    {
        var para1 = new string('x', 400);
        var para2 = new string('y', 400);
        var text = para1 + "\n\n" + para2;
        var chunks = _chunker.Split(text, maxTokensPerChunk: 150);
        chunks.Should().HaveCount(2);
        chunks[0].Should().Contain(para1);
        chunks[1].Should().Contain(para2);
    }

    [Fact]
    public void Split_SplitsBeforeTableSeparator()
    {
        var intro = new string('i', 400);
        var table = "| Name | CR |\n|------|----|\n| Goblin | 1/4 |";
        var text = intro + "\n" + table;
        var chunks = _chunker.Split(text, maxTokensPerChunk: 110);
        chunks.Should().HaveCount(2);
        chunks[1].Should().Contain("| Name | CR |");
    }

    [Fact]
    public void Split_NeverEmitsEmptyChunks()
    {
        var text = "\n\n" + new string('z', 900) + "\n\n\n\n" + new string('w', 900) + "\n\n";
        var chunks = _chunker.Split(text, maxTokensPerChunk: 150);
        chunks.Should().NotContain(c => string.IsNullOrWhiteSpace(c));
    }

    [Fact]
    public void Split_NoBoundaries_SplitsAtLineGranularity()
    {
        var lines = Enumerable.Range(0, 10).Select(i => new string((char)('a' + i), 200));
        var text = string.Join("\n", lines);
        var chunks = _chunker.Split(text, maxTokensPerChunk: 150);
        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(c => c.Length / 4 <= 150);
        string.Concat(chunks.Select(c => c.Replace("\n", "")))
            .Should().Be(text.Replace("\n", ""));
    }

    [Fact]
    public void Split_SingleOversizedLine_IsHardSplitToBudget()
    {
        var line = new string('x', 2000);
        var chunks = _chunker.Split(line, maxTokensPerChunk: 150);
        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(c => c.Length / 4 <= 150);
        string.Concat(chunks).Should().Be(line);
    }
}
