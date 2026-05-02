using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Writer;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class PdfPigBookmarkReaderTests
{
    private static readonly PdfPigBookmarkReader Sut =
        new(NullLogger<PdfPigBookmarkReader>.Instance);

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static ExplicitDestination FitPage(int pageNumber) =>
        new(pageNumber, ExplicitDestinationType.FitPage, ExplicitDestinationCoordinates.Empty);

    /// <summary>
    /// Builds an in-memory PDF with the given bookmark tree and opens it
    /// via a temporary file so that <see cref="PdfPigBookmarkReader"/> can
    /// call <c>PdfDocument.Open(filePath)</c>.
    /// </summary>
    private static string BuildTempPdf(int pageCount, Bookmarks? bookmarks = null)
    {
        var builder = new PdfDocumentBuilder();
        for (var i = 0; i < pageCount; i++)
            builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);

        if (bookmarks is not null)
            builder.Bookmarks = bookmarks;

        var bytes = builder.Build();
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ---------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------

    [Fact]
    public void ReadBookmarks_NoBookmarks_ReturnsEmptyList()
    {
        var path = BuildTempPdf(pageCount: 1);
        try
        {
            var result = Sut.ReadBookmarks(path);

            Assert.Empty(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadBookmarks_WithBookmarks_ReturnsFlatList()
    {
        var node1 = new DocumentBookmarkNode("Chapter 1", 0, FitPage(1), []);
        var node2 = new DocumentBookmarkNode("Chapter 2", 0, FitPage(2), []);
        var bookmarks = new Bookmarks([node1, node2]);

        var path = BuildTempPdf(pageCount: 2, bookmarks);
        try
        {
            var result = Sut.ReadBookmarks(path);

            Assert.Equal(2, result.Count);
            Assert.Contains(result, b => b.Title == "Chapter 1" && b.PageNumber == 1);
            Assert.Contains(result, b => b.Title == "Chapter 2" && b.PageNumber == 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadBookmarks_NestedBookmarks_RecursesAllLevels()
    {
        // Part 1 (page 1)
        //   Chapter 1 (page 2)
        //     Section 1.1 (page 3)
        // Part 2 (page 4)
        //   Chapter 2 (page 5)
        var section = new DocumentBookmarkNode("Section 1.1", 2, FitPage(3), []);
        var chapter1 = new DocumentBookmarkNode("Chapter 1", 1, FitPage(2), [section]);
        var part1 = new DocumentBookmarkNode("Part 1", 0, FitPage(1), [chapter1]);
        var chapter2 = new DocumentBookmarkNode("Chapter 2", 1, FitPage(5), []);
        var part2 = new DocumentBookmarkNode("Part 2", 0, FitPage(4), [chapter2]);
        var bookmarks = new Bookmarks([part1, part2]);

        var path = BuildTempPdf(pageCount: 5, bookmarks);
        try
        {
            var result = Sut.ReadBookmarks(path);

            Assert.Equal(5, result.Count);
            Assert.Contains(result, b => b.Title == "Part 1" && b.PageNumber == 1);
            Assert.Contains(result, b => b.Title == "Chapter 1" && b.PageNumber == 2);
            Assert.Contains(result, b => b.Title == "Section 1.1" && b.PageNumber == 3);
            Assert.Contains(result, b => b.Title == "Part 2" && b.PageNumber == 4);
            Assert.Contains(result, b => b.Title == "Chapter 2" && b.PageNumber == 5);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
