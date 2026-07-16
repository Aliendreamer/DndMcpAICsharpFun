using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class StructureBlockExtractorTests
{
    private static readonly PdfStructureDocument SampleDoc = new(
        Markdown: "irrelevant",
        Items: new List<PdfStructureItem>
        {
            new("section_header", "Wizard",       112, 1),
            new("paragraph",      "Scholarly magic-user.", 112, null),
            new("paragraph",      "Spell list...", 113, null),
            new("paragraph",      "More wizardry.", 113, null),
        });

    [Fact]
    public async Task ExtractBlocksAsync_MapsItemsToBlocks_PreservingPageAndOrder()
    {
        var converter = Substitute.For<IPdfStructureConverter>();
        converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SampleDoc));
        var sut = new StructureBlockExtractor(converter, NullLogger<StructureBlockExtractor>.Instance);

        var result = await sut.ExtractBlocksAsync("/tmp/fake.pdf");
        var blocks = result.Blocks.ToList();

        Assert.Equal(4, blocks.Count);
        Assert.Equal("Wizard", blocks[0].Text);
        Assert.Equal(112, blocks[0].PageNumber);
        Assert.Equal(0, blocks[0].Order);
        Assert.Equal(112, blocks[1].PageNumber);
        Assert.Equal(1, blocks[1].Order);
        Assert.Equal(113, blocks[2].PageNumber);
        Assert.Equal(0, blocks[2].Order);  // per-page order resets
        Assert.Equal(113, blocks[3].PageNumber);
        Assert.Equal(1, blocks[3].Order);

        // the section_header item is surfaced in Headings
        Assert.Single(result.Headings);
        Assert.Equal("Wizard", result.Headings[0].Text);
        Assert.Equal(112, result.Headings[0].PageNumber);
    }

    [Fact]
    public async Task ExtractBlocksAsync_WhitespaceItem_Skipped()
    {
        var doc = new PdfStructureDocument("", new List<PdfStructureItem>
        {
            new("paragraph", "real",   1, null),
            new("paragraph", "   ",    1, null),
            new("paragraph", "real 2", 1, null),
        });
        var converter = Substitute.For<IPdfStructureConverter>();
        converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(doc));
        var sut = new StructureBlockExtractor(converter, NullLogger<StructureBlockExtractor>.Instance);

        var result = await sut.ExtractBlocksAsync("/tmp/x.pdf");
        var blocks = result.Blocks.ToList();

        Assert.Equal(2, blocks.Count);
        Assert.Equal("real", blocks[0].Text);
        Assert.Equal("real 2", blocks[1].Text);
        Assert.Empty(result.Headings);  // no section_header items → empty (not null)
    }
}