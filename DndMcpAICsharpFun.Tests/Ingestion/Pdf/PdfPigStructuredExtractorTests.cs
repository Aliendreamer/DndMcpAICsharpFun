using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class PdfPigStructuredExtractorTests
{
    private static PdfPigStructuredExtractor BuildSut(int minChars = 0) =>
        new(Options.Create(new IngestionOptions { MinPageCharacters = minChars }),
            NullLogger<PdfPigStructuredExtractor>.Instance);

    [Fact]
    public void InferHeadingLevels_ThreeDistinctSizes_AssignsH1H2Body()
    {
        var blocks = new[]
        {
            (FontSize: 18.0, Text: "Big Heading"),
            (FontSize: 14.0, Text: "Sub Heading"),
            (FontSize: 10.0, Text: "Body text here"),
            (FontSize: 10.0, Text: "More body text"),
        };

        var result = PdfPigStructuredExtractor.InferHeadingLevels(blocks);

        Assert.Equal(4, result.Count);
        Assert.Equal("h1",   result[0].Level);
        Assert.Equal("h2",   result[1].Level);
        Assert.Equal("body", result[2].Level);
        Assert.Equal("body", result[3].Level);
    }

    [Fact]
    public void InferHeadingLevels_SingleFontSize_AllBody()
    {
        var blocks = new[]
        {
            (FontSize: 12.0, Text: "Line one"),
            (FontSize: 12.0, Text: "Line two"),
        };

        var result = PdfPigStructuredExtractor.InferHeadingLevels(blocks);

        Assert.All(result, b => Assert.Equal("body", b.Level));
    }

    [Fact]
    public void InferHeadingLevels_FourDistinctSizes_AssignsH1H2H3Body()
    {
        var blocks = new[]
        {
            (FontSize: 24.0, Text: "Chapter"),
            (FontSize: 18.0, Text: "Section"),
            (FontSize: 14.0, Text: "Sub-section"),
            (FontSize: 10.0, Text: "Paragraph"),
        };

        var result = PdfPigStructuredExtractor.InferHeadingLevels(blocks);

        Assert.Equal("h1",   result[0].Level);
        Assert.Equal("h2",   result[1].Level);
        Assert.Equal("h3",   result[2].Level);
        Assert.Equal("body", result[3].Level);
    }

    [Fact]
    public void InferHeadingLevels_EmptyInput_ReturnsEmpty()
    {
        var result = PdfPigStructuredExtractor.InferHeadingLevels([]);
        Assert.Empty(result);
    }

    [Fact]
    public void InferHeadingLevels_OrderFieldMatchesPosition()
    {
        var blocks = new[]
        {
            (FontSize: 12.0, Text: "A"),
            (FontSize: 12.0, Text: "B"),
            (FontSize: 12.0, Text: "C"),
        };

        var result = PdfPigStructuredExtractor.InferHeadingLevels(blocks);

        Assert.Equal([1, 2, 3], result.Select(b => b.Order));
    }

    [Fact]
    public void ExtractPages_SinglePage_ReturnsOneStructuredPage()
    {
        var path = BuildTempPdf(b =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            var page = b.AddPage(PageSize.A4);
            page.AddText("Fireball", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        });
        try
        {
            var sut = BuildSut();
            var result = sut.ExtractPages(path).ToList();
            Assert.Single(result);
            Assert.Equal(1, result[0].PageNumber);
            Assert.Contains("Fireball", result[0].RawText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExtractSinglePage_ValidPage_ReturnsStructuredPage()
    {
        var path = BuildTempPdf(b =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            b.AddPage(PageSize.A4);
            var p2 = b.AddPage(PageSize.A4);
            p2.AddText("Spell", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        });
        try
        {
            var sut = BuildSut();
            var result = sut.ExtractSinglePage(path, 2);
            Assert.NotNull(result);
            Assert.Equal(2, result!.PageNumber);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExtractSinglePage_PageOutOfRange_ReturnsNull()
    {
        var path = BuildTempPdf(b => b.AddPage(PageSize.A4));
        try
        {
            var result = BuildSut().ExtractSinglePage(path, 99);
            Assert.Null(result);
        }
        finally { File.Delete(path); }
    }

    private static string BuildTempPdf(Action<PdfDocumentBuilder> configure)
    {
        var builder = new PdfDocumentBuilder();
        configure(builder);
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, builder.Build());
        return path;
    }
}
