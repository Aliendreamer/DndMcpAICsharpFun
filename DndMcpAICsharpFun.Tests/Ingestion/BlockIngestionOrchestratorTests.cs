using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Marker;
using DndMcpAICsharpFun.Infrastructure.Ingestion;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class BlockIngestionOrchestratorTests
{
    /// <summary>
    /// When block extraction throws (e.g. the Marker service is unreachable),
    /// the failure message persisted to the database must name the marker service
    /// and include the configured Marker:Url so operators know where to look.
    /// </summary>
    [Fact]
    public async Task IngestBlocksAsync_ExtractionThrows_FailureMessageMentionsMarkerUrl()
    {
        const string markerUrl = "http://marker:5002";
        const int bookId = 7;

        var record = new IngestionRecord
        {
            Id          = bookId,
            FilePath    = "/books/test.pdf",
            FileName    = "test.pdf",
            FileHash    = "abc123",
            DisplayName = "Test Book",
            Version     = "5e",
        };

        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

        // Simulate Marker being unreachable.
        var blockExtractor = Substitute.For<IPdfBlockExtractor>();
        blockExtractor
            .ExtractBlocks(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new HttpRequestException($"Connection refused to {markerUrl}"));

        // Bookmarks must be non-empty so we get past the bookmark guard.
        var bookmarkReader = Substitute.For<IPdfBookmarkReader>();
        bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
            new List<PdfBookmark> { new("Intro", 1) });

        var markerOptions = Options.Create(new MarkerOptions { Url = markerUrl });

        var sut = new BlockIngestionOrchestrator(
            tracker:        tracker,
            bookmarkReader: bookmarkReader,
            blockExtractor: blockExtractor,
            embedding:      Substitute.For<IEmbeddingService>(),
            vectorStore:    Substitute.For<IVectorStoreService>(),
            markerOptions:  markerOptions,
            logger:         NullLogger<BlockIngestionOrchestrator>.Instance);

        await sut.IngestBlocksAsync(bookId);

        // The failure message must mention the marker service and its configured URL.
        await tracker.Received(1).MarkFailedAsync(
            bookId,
            Arg.Is<string>(msg =>
                msg.Contains("marker", StringComparison.OrdinalIgnoreCase) &&
                msg.Contains(markerUrl, StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }
}
