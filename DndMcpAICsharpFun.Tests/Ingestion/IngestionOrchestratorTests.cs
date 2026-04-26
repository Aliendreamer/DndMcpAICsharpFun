using System.Security.Cryptography;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Chunking;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class IngestionOrchestratorTests : IDisposable
{
    private readonly IIngestionTracker _tracker = Substitute.For<IIngestionTracker>();
    private readonly IPdfTextExtractor _extractor = Substitute.For<IPdfTextExtractor>();
    private readonly IEmbeddingIngestor _embeddingIngestor = Substitute.For<IEmbeddingIngestor>();
    private readonly string _tempFile;

    public IngestionOrchestratorTests()
    {
        _tempFile = Path.GetTempFileName();
        File.WriteAllText(_tempFile, "dummy pdf content");
    }

    public void Dispose() => File.Delete(_tempFile);

    private IngestionOrchestrator BuildSut()
    {
        var detectors = new IPatternDetector[] { new SpellPatternDetector() };
        var detector = new ContentCategoryDetector(detectors);
        var opts = Options.Create(new IngestionOptions { MaxChunkTokens = 512, OverlapTokens = 64 });
        var chunker = new DndChunker(detector, opts);
        return new IngestionOrchestrator(
            _tracker, _extractor, chunker, _embeddingIngestor,
            NullLogger<IngestionOrchestrator>.Instance);
    }

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, CancellationToken.None);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public async Task IngestBookAsync_RecordNotFound_DoesNotCallExtractor()
    {
        _tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);
        var sut = BuildSut();

        await sut.IngestBookAsync(1);

        _extractor.DidNotReceive().ExtractPages(Arg.Any<string>());
    }

    [Fact]
    public async Task IngestBookAsync_CompletedUnchangedHash_SkipsExtraction()
    {
        var hash = await HashFileAsync(_tempFile);
        var record = new IngestionRecord
        {
            Id = 1, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = hash, SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Completed
        };
        _tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);
        var sut = BuildSut();

        await sut.IngestBookAsync(1);

        _extractor.DidNotReceive().ExtractPages(Arg.Any<string>());
        await _embeddingIngestor.DidNotReceive()
            .IngestAsync(Arg.Any<IList<ContentChunk>>(), Arg.Any<string>());
    }

    [Fact]
    public async Task IngestBookAsync_PendingRecord_IngestsAndMarkCompleted()
    {
        var record = new IngestionRecord
        {
            Id = 2, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = "stale-hash", SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Pending
        };
        _tracker.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(record);
        _extractor.ExtractPages(_tempFile)
            .Returns([(1, "Casting Time: 1 action\nRange: 150 feet\nDuration: Instantaneous")]);
        var sut = BuildSut();

        await sut.IngestBookAsync(2);

        _extractor.Received(1).ExtractPages(_tempFile);
        await _embeddingIngestor.Received(1)
            .IngestAsync(Arg.Any<IList<ContentChunk>>(), Arg.Any<string>());
        await _tracker.Received(1).MarkCompletedAsync(2, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestBookAsync_ExtractorThrows_MarksFailedAsync()
    {
        var record = new IngestionRecord
        {
            Id = 3, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = "stale-hash", SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Pending
        };
        _tracker.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(record);
        _extractor.When(x => x.ExtractPages(Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("pdf error"));
        var sut = BuildSut();

        await sut.IngestBookAsync(3);

        await _tracker.Received(1).MarkFailedAsync(3, "pdf error", Arg.Any<CancellationToken>());
        await _embeddingIngestor.DidNotReceive()
            .IngestAsync(Arg.Any<IList<ContentChunk>>(), Arg.Any<string>());
    }
}
