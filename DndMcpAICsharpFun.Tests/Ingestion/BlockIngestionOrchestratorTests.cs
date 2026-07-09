using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Ingestion;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class BlockIngestionOrchestratorTests
{
    /// <summary>
    /// When block extraction throws (e.g. the MinerU service is unreachable),
    /// the failure message persisted to the database must name the MinerU service
    /// so operators know where to look.
    /// </summary>
    [Fact]
    public async Task IngestBlocksAsync_ExtractionThrows_FailureMessageMentionsMinerU()
    {
        const int bookId = 7;

        var record = new IngestionRecord
        {
            Id = bookId,
            FilePath = "/books/test.pdf",
            FileName = "test.pdf",
            FileHash = "abc123",
            DisplayName = "Test Book",
            Version = "5e",
        };

        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

        // Simulate the PDF conversion (MinerU) being unreachable.
        var blockExtractor = Substitute.For<IPdfBlockExtractor>();
        blockExtractor
            .ExtractBlocksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<PdfBlock>>(
                new HttpRequestException("Connection refused to http://mineru:8000")));

        // Bookmarks must be non-empty so we get past the bookmark guard.
        var bookmarkReader = Substitute.For<IPdfBookmarkReader>();
        bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
            new List<PdfBookmark> { new("Intro", 1) });

        var sut = new BlockIngestionOrchestrator(
            tracker: tracker,
            bookmarkReader: bookmarkReader,
            blockExtractor: blockExtractor,
            embedding: Substitute.For<IEmbeddingService>(),
            vectorStore: Substitute.For<IVectorStoreService>(),
            bm25Stats: Substitute.For<IBm25CorpusStats>(),
            logger: NullLogger<BlockIngestionOrchestrator>.Instance);

        await sut.IngestBlocksAsync(bookId);

        // The failure message must mention the MinerU service so operators know where to look.
        await tracker.Received(1).MarkFailedAsync(
            bookId,
            Arg.Is<string>(msg =>
                msg.Contains("MinerU", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }
}