using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class PdfPigTextExtractorTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static PdfPigTextExtractor BuildSut(CapturingLogger<PdfPigTextExtractor>? logger = null, int minChars = 0)
        => new(
            Options.Create(new IngestionOptions { MinPageCharacters = minChars }),
            logger ?? new CapturingLogger<PdfPigTextExtractor>());

    private static string BuildTempPdf(Action<PdfDocumentBuilder> configure)
    {
        var builder = new PdfDocumentBuilder();
        configure(builder);
        var bytes = builder.Build();
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ExtractPages_SinglePage_ReturnsOnePageWithText()
    {
        var path = BuildTempPdf(b =>
        {
            var page = b.AddPage(PageSize.A4);
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText("Fireball", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        });
        try
        {
            var sut = BuildSut();
            var results = sut.ExtractPages(path).ToList();

            Assert.Single(results);
            Assert.Equal(1, results[0].PageNumber);
            Assert.Contains("Fireball", results[0].Text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExtractPages_MultiPage_ReturnsAllPagesInOrder()
    {
        var path = BuildTempPdf(b =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            for (var i = 1; i <= 3; i++)
            {
                var page = b.AddPage(PageSize.A4);
                page.AddText($"Page{i}", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
            }
        });
        try
        {
            var sut = BuildSut();
            var results = sut.ExtractPages(path).ToList();

            Assert.Equal(3, results.Count);
            Assert.Equal([1, 2, 3], results.Select(r => r.PageNumber));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExtractPages_PageBelowMinChars_EmitsDebugLog()
    {
        var path = BuildTempPdf(b =>
        {
            // Page with no text — extracted text will be empty (length = 0)
            b.AddPage(PageSize.A4);
        });
        try
        {
            var logger = new CapturingLogger<PdfPigTextExtractor>();
            var sut = BuildSut(logger, minChars: 100); // 0 < 100, triggers log
            _ = sut.ExtractPages(path).ToList(); // materialize

            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Debug && e.Message.Contains("Sparse page"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExtractPages_PageMeetsMinChars_NoSparseLog()
    {
        var path = BuildTempPdf(b =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            var page = b.AddPage(PageSize.A4);
            // Add enough text to exceed minChars=5
            page.AddText("Hello World This Is Enough Text", 12,
                new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        });
        try
        {
            var logger = new CapturingLogger<PdfPigTextExtractor>();
            var sut = BuildSut(logger, minChars: 5);
            _ = sut.ExtractPages(path).ToList();

            Assert.DoesNotContain(logger.Entries, e =>
                e.Level == LogLevel.Debug && e.Message.Contains("Sparse page"));
        }
        finally { File.Delete(path); }
    }
}
