using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class PdfPigBlockExtractorTests
{
    private static readonly PdfPigBlockExtractor Sut = new();

    private static string BuildSinglePagePdf(Action<PdfDocumentBuilder, PdfPageBuilder> populate)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        populate(builder, page);
        var bytes = builder.Build();
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ExtractBlocks_EmptyPage_ReturnsNothing()
    {
        var path = BuildSinglePagePdf((_, _) => { });
        try
        {
            var result = Sut.ExtractBlocks(path).ToList();
            Assert.Empty(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExtractBlocks_SinglePageWithText_ReturnsAtLeastOneBlock()
    {
        var path = BuildSinglePagePdf((b, page) =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText("Spells of the Wizard", 14, new PdfPoint(50, 750), font);
            page.AddText("Fireball is a powerful evocation spell.", 12, new PdfPoint(50, 720), font);
        });
        try
        {
            var result = Sut.ExtractBlocks(path).ToList();

            Assert.NotEmpty(result);
            Assert.All(result, b => Assert.Equal(1, b.PageNumber));
            Assert.All(result, b => Assert.False(string.IsNullOrWhiteSpace(b.Text)));
            Assert.Contains(result, b => b.Text.Contains("Spells", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExtractBlocks_OrdersBlocksWithMonotonicOrderPerPage()
    {
        var path = BuildSinglePagePdf((b, page) =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText("Heading", 16, new PdfPoint(50, 750), font);
            page.AddText("First paragraph of body text.", 11, new PdfPoint(50, 700), font);
            page.AddText("Second paragraph of body text.", 11, new PdfPoint(50, 650), font);
        });
        try
        {
            var result = Sut.ExtractBlocks(path).ToList();

            Assert.NotEmpty(result);
            for (var i = 0; i < result.Count; i++)
                Assert.Equal(i, result[i].Order);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExtractBlocks_AssignsCorrectPageNumberAcrossPages()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var p1 = builder.AddPage(PageSize.A4);
        var p2 = builder.AddPage(PageSize.A4);
        p1.AddText("Page one content.", 12, new PdfPoint(50, 750), font);
        p2.AddText("Page two content.", 12, new PdfPoint(50, 750), font);
        var bytes = builder.Build();
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        try
        {
            var result = Sut.ExtractBlocks(path).ToList();

            Assert.Contains(result, b => b.PageNumber == 1 && b.Text.Contains("one", StringComparison.Ordinal));
            Assert.Contains(result, b => b.PageNumber == 2 && b.Text.Contains("two", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PdfBlock_ValueEquality()
    {
        var box = new PdfRectangle(0, 0, 100, 100);
        var a = new PdfBlock("text", 1, 0, box);
        var b = new PdfBlock("text", 1, 0, box);
        Assert.Equal(a, b);
    }
}
