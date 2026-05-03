using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class PdfPigBlockExtractorTests
{
    private static PdfPigBlockExtractor Build(string segmenter = "docstrum") =>
        new(Options.Create(new IngestionOptions { BlockSegmenter = segmenter }),
            NullLogger<PdfPigBlockExtractor>.Instance);

    private static readonly PdfPigBlockExtractor Sut = Build();

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

    [Fact]
    public void Default_UsesDocstrum_ProducesBlocks()
    {
        var sut = Build();   // no explicit segmenter, default is docstrum
        var path = BuildSinglePagePdf((b, page) =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText("Default segmenter test.", 12, new PdfPoint(50, 750), font);
        });
        try
        {
            var result = sut.ExtractBlocks(path).ToList();
            Assert.NotEmpty(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void XyCutSelected_ProducesBlocks()
    {
        var sut = Build("xycut");
        var path = BuildSinglePagePdf((b, page) =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText("Recursive XY-Cut segmenter test.", 12, new PdfPoint(50, 750), font);
        });
        try
        {
            var result = sut.ExtractBlocks(path).ToList();
            Assert.NotEmpty(result);
            Assert.All(result, blk => Assert.Equal(1, blk.PageNumber));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InvalidValue_FallsBackToDocstrum_AndLogsWarning()
    {
        var captured = new CapturingLogger<PdfPigBlockExtractor>();
        var sut = new PdfPigBlockExtractor(
            Options.Create(new IngestionOptions { BlockSegmenter = "nonsense" }),
            captured);
        var path = BuildSinglePagePdf((b, page) =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText("Fallback segmenter test.", 12, new PdfPoint(50, 750), font);
        });
        try
        {
            var result = sut.ExtractBlocks(path).ToList();
            Assert.NotEmpty(result);
            Assert.Contains(captured.Entries, e =>
                e.Level == LogLevel.Warning && e.Message.Contains("nonsense", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
