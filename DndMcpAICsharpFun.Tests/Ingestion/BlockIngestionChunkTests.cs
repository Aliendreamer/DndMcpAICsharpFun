using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class BlockIngestionChunkTests
{
    private static IngestionRecord Record() => new()
    {
        DisplayName = "Test Book",
        Version = "Edition2014",
        BookType = BookType.Setting,
        FivetoolsSourceKey = "TST",
    };

    private static readonly IReadOnlyList<PdfBlock> Blocks =
    [
        new PdfBlock(new string('a', 60), PageNumber: 1, Order: 0),
        new PdfBlock(new string('b', 60), PageNumber: 2, Order: 1),
        new PdfBlock(new string('c', 60), PageNumber: 3, Order: 2),
    ];

    [Fact]
    public void BuildChunks_returns_empty_when_no_block_page_falls_in_any_section()
    {
        // Bookmark-derived TOC whose only section starts at page 100 — every block (pages 1-3) misses.
        var bookmarkToc = new TocCategoryMap([new TocSectionEntry("Chapter 1", ContentCategory.Rule, 100)]);

        var chunks = BlockIngestionOrchestrator.BuildChunks(Blocks, bookmarkToc, Record(), DndVersion.Edition2014);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void BuildChunks_produces_page_aligned_chunks_from_a_full_coverage_toc()
    {
        // Heading-derived TOC starting at page 1 covers every block.
        var headingToc = new TocCategoryMap([new TocSectionEntry("Welcome", ContentCategory.Rule, 1)]);

        var chunks = BlockIngestionOrchestrator.BuildChunks(Blocks, headingToc, Record(), DndVersion.Edition2014);

        chunks.Should().HaveCount(3);
        chunks.Should().OnlyContain(c => c.Metadata.SectionTitle == "Welcome");
        chunks.Select(c => c.Metadata.GlobalIndex).Should().Equal(0, 1, 2);
        chunks.Should().OnlyContain(c => c.Metadata.SourceKey == "TST");
    }
}