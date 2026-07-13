using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Ingestion;
using DndMcpAICsharpFun.Infrastructure.Search;

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

    /// <summary>
    /// A record's <c>FivetoolsSourceKey</c> (e.g. "DMG") must be carried onto every
    /// <see cref="BlockMetadata.SourceKey"/> for the blocks it produces, so retrieval can later
    /// filter blocks by a stable source key instead of the free-text display name.
    /// </summary>
    [Fact]
    public async Task IngestBlocksAsync_RecordHasFivetoolsSourceKey_BlockMetadataCarriesSourceKey()
    {
        var chunks = await RunIngestAndCaptureChunksAsync(fivetoolsSourceKey: "DMG");

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal("DMG", c.Chunk.Metadata.SourceKey));
    }

    /// <summary>
    /// When a record has no <c>FivetoolsSourceKey</c> (homebrew/non-5etools books), the resulting
    /// block metadata must leave <see cref="BlockMetadata.SourceKey"/> null rather than inventing one.
    /// </summary>
    [Fact]
    public async Task IngestBlocksAsync_RecordHasNoFivetoolsSourceKey_BlockMetadataSourceKeyIsNull()
    {
        var chunks = await RunIngestAndCaptureChunksAsync(fivetoolsSourceKey: null);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Null(c.Chunk.Metadata.SourceKey));
    }

    /// <summary>
    /// Runs <see cref="BlockIngestionOrchestrator.IngestBlocksAsync"/> end-to-end against a single
    /// synthetic block that clears every guard (bookmarks present, block long enough, TOC match),
    /// and captures the chunks handed to <see cref="IVectorStoreService.UpsertBlocksAsync"/>.
    /// </summary>
    private static async Task<IList<(BlockChunk Chunk, float[] Vector, SparseVector Sparse, string FileHash)>>
        RunIngestAndCaptureChunksAsync(string? fivetoolsSourceKey)
    {
        const int bookId = 42;

        var record = new IngestionRecord
        {
            Id = bookId,
            FilePath = "/books/test.pdf",
            FileName = "test.pdf",
            FileHash = "abc123",
            DisplayName = "Test Book",
            Version = "5e",
            FivetoolsSourceKey = fivetoolsSourceKey,
        };

        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

        var bookmarkReader = Substitute.For<IPdfBookmarkReader>();
        bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(
            new List<PdfBookmark> { new("Intro", 1) });

        var blockExtractor = Substitute.For<IPdfBlockExtractor>();
        blockExtractor.ExtractBlocksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<PdfBlock>
            {
                new("This is a sufficiently long synthetic block of prose for the ingestion pipeline.", 1, 0),
            });

        var embedding = Substitute.For<IEmbeddingService>();
        embedding.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IList<float[]>>(
                ((IList<string>)call[0]).Select(_ => new float[] { 0.1f, 0.2f, 0.3f, 0.4f }).ToList()));

        var bm25Stats = Substitute.For<IBm25CorpusStats>();
        bm25Stats.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new Bm25GlobalStats(1, 1, new Dictionary<string, long>()));

        IList<(BlockChunk Chunk, float[] Vector, SparseVector Sparse, string FileHash)>? captured = null;
        var vectorStore = Substitute.For<IVectorStoreService>();
        vectorStore
            .UpsertBlocksAsync(
                Arg.Do<IList<(BlockChunk Chunk, float[] Vector, SparseVector Sparse, string FileHash)>>(
                    list => captured = list),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = new BlockIngestionOrchestrator(
            tracker: tracker,
            bookmarkReader: bookmarkReader,
            blockExtractor: blockExtractor,
            embedding: embedding,
            vectorStore: vectorStore,
            bm25Stats: bm25Stats,
            logger: NullLogger<BlockIngestionOrchestrator>.Instance);

        await sut.IngestBlocksAsync(bookId);

        Assert.NotNull(captured);
        return captured!;
    }
}