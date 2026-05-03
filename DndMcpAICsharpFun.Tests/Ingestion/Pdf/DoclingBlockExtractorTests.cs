using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class DoclingBlockExtractorTests
{
    private static readonly DoclingDocument SampleDoc = new(
        Markdown: "irrelevant",
        Items: new List<DoclingItem>
        {
            new("section_header", "Wizard",       112, 1),
            new("paragraph",      "Scholarly magic-user.", 112, null),
            new("paragraph",      "Spell list...", 113, null),
            new("paragraph",      "More wizardry.", 113, null),
        });

    [Fact]
    public void ExtractBlocks_MapsItemsToBlocks_PreservingPageAndOrder()
    {
        var converter = Substitute.For<IDoclingPdfConverter>();
        converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SampleDoc));
        var sut = new DoclingBlockExtractor(converter, NullLogger<DoclingBlockExtractor>.Instance);

        var result = sut.ExtractBlocks("/tmp/fake.pdf").ToList();

        Assert.Equal(4, result.Count);
        Assert.Equal("Wizard", result[0].Text);
        Assert.Equal(112, result[0].PageNumber);
        Assert.Equal(0, result[0].Order);
        Assert.Equal(112, result[1].PageNumber);
        Assert.Equal(1, result[1].Order);
        Assert.Equal(113, result[2].PageNumber);
        Assert.Equal(0, result[2].Order);  // per-page order resets
        Assert.Equal(113, result[3].PageNumber);
        Assert.Equal(1, result[3].Order);
    }

    [Fact]
    public void ExtractBlocks_WhitespaceItem_Skipped()
    {
        var doc = new DoclingDocument("", new List<DoclingItem>
        {
            new("paragraph", "real",   1, null),
            new("paragraph", "   ",    1, null),
            new("paragraph", "real 2", 1, null),
        });
        var converter = Substitute.For<IDoclingPdfConverter>();
        converter.ConvertAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(doc));
        var sut = new DoclingBlockExtractor(converter, NullLogger<DoclingBlockExtractor>.Instance);

        var result = sut.ExtractBlocks("/tmp/x.pdf").ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("real",   result[0].Text);
        Assert.Equal("real 2", result[1].Text);
    }
}
