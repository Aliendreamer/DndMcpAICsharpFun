using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Chunking;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class IngestionOrchestratorTests : IDisposable
{
    private readonly IIngestionTracker _tracker = Substitute.For<IIngestionTracker>();
    private readonly IPdfTextExtractor _extractor = Substitute.For<IPdfTextExtractor>();
    private readonly IEmbeddingIngestor _embeddingIngestor = Substitute.For<IEmbeddingIngestor>();
    private readonly IVectorStoreService _vectorStore = Substitute.For<IVectorStoreService>();
    private readonly ILlmClassifier _classifier = Substitute.For<ILlmClassifier>();
    private readonly ILlmEntityExtractor _entityExtractor = Substitute.For<ILlmEntityExtractor>();
    private readonly IEntityJsonStore _jsonStore = Substitute.For<IEntityJsonStore>();
    private readonly IJsonIngestionPipeline _jsonPipeline = Substitute.For<IJsonIngestionPipeline>();
    private readonly string _tempFile;

    public IngestionOrchestratorTests()
    {
        _tempFile = Path.GetTempFileName();
        File.WriteAllText(_tempFile, "dummy pdf content");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    private IngestionOrchestrator BuildSut()
    {
        var detectors = new IPatternDetector[] { new SpellPatternDetector() };
        var detector = new ContentCategoryDetector(detectors);
        var opts = Options.Create(new IngestionOptions { MaxChunkTokens = 512, OverlapTokens = 64 });
        var chunker = new DndChunker(detector, opts);
        return new IngestionOrchestrator(
            _tracker, _extractor, chunker, _embeddingIngestor, _vectorStore,
            _classifier, _entityExtractor, _jsonStore, _jsonPipeline, opts,
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
        await _tracker.DidNotReceive().MarkHashAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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
        _tracker.GetCompletedByHashAsync(Arg.Any<string>(), 2, Arg.Any<CancellationToken>())
            .Returns((IngestionRecord?)null);
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
        _tracker.GetCompletedByHashAsync(Arg.Any<string>(), 3, Arg.Any<CancellationToken>())
            .Returns((IngestionRecord?)null);
        _extractor.When(x => x.ExtractPages(Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("pdf error"));
        var sut = BuildSut();

        await sut.IngestBookAsync(3);

        await _tracker.Received(1).MarkFailedAsync(3, "pdf error", Arg.Any<CancellationToken>());
        await _embeddingIngestor.DidNotReceive()
            .IngestAsync(Arg.Any<IList<ContentChunk>>(), Arg.Any<string>());
    }

    [Fact]
    public async Task IngestBookAsync_DuplicateHash_MarksDuplicateAndSkipsExtraction()
    {
        var record = new IngestionRecord
        {
            Id = 10, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = string.Empty, SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Pending
        };
        var existing = new IngestionRecord { Id = 5, Status = IngestionStatus.Completed };
        _tracker.GetByIdAsync(10, Arg.Any<CancellationToken>()).Returns(record);
        _tracker.GetCompletedByHashAsync(Arg.Any<string>(), 10, Arg.Any<CancellationToken>())
            .Returns(existing);
        var sut = BuildSut();

        await sut.IngestBookAsync(10);

        await _tracker.Received(1).MarkDuplicateAsync(10, Arg.Any<CancellationToken>());
        _extractor.DidNotReceive().ExtractPages(Arg.Any<string>());
        await _embeddingIngestor.DidNotReceive()
            .IngestAsync(Arg.Any<IList<ContentChunk>>(), Arg.Any<string>());
        await _tracker.Received(1).MarkHashAsync(10, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestBookAsync_PendingRecord_CallsMarkHashAsync()
    {
        var record = new IngestionRecord
        {
            Id = 11, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = string.Empty, SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Pending
        };
        _tracker.GetByIdAsync(11, Arg.Any<CancellationToken>()).Returns(record);
        _tracker.GetCompletedByHashAsync(Arg.Any<string>(), 11, Arg.Any<CancellationToken>())
            .Returns((IngestionRecord?)null);
        _extractor.ExtractPages(_tempFile)
            .Returns([(1, "Casting Time: 1 action\nRange: 150 feet\nDuration: Instantaneous")]);
        var sut = BuildSut();

        await sut.IngestBookAsync(11);

        await _tracker.Received(1).MarkHashAsync(11, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteBookAsync_RecordNotFound_ReturnsNotFound()
    {
        _tracker.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);
        var sut = BuildSut();

        var result = await sut.DeleteBookAsync(99);

        Assert.Equal(DeleteBookResult.NotFound, result);
        await _vectorStore.DidNotReceive().DeleteByHashAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteBookAsync_RecordProcessing_ReturnsConflict()
    {
        var record = new IngestionRecord
        {
            Id = 1, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = "abc", SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Processing
        };
        _tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);
        var sut = BuildSut();

        var result = await sut.DeleteBookAsync(1);

        Assert.Equal(DeleteBookResult.Conflict, result);
        await _vectorStore.DidNotReceive().DeleteByHashAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteBookAsync_CompletedRecord_DeletesVectorsFileSqlite()
    {
        var record = new IngestionRecord
        {
            Id = 2, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = "abc123", SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Completed, ChunkCount = 10
        };
        _tracker.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(record);
        _tracker.DeleteAsync(2, Arg.Any<CancellationToken>()).Returns(true);
        var sut = BuildSut();

        var result = await sut.DeleteBookAsync(2);

        Assert.Equal(DeleteBookResult.Deleted, result);
        await _vectorStore.Received(1).DeleteByHashAsync("abc123", 10, Arg.Any<CancellationToken>());
        _jsonStore.Received(1).DeleteAllPages(2);
        await _tracker.Received(1).DeleteAsync(2, Arg.Any<CancellationToken>());
        Assert.False(File.Exists(_tempFile), "Physical file should have been deleted");
    }

    [Fact]
    public async Task DeleteBookAsync_PendingRecord_SkipsVectorsDeletesSqlite()
    {
        var record = new IngestionRecord
        {
            Id = 5, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = string.Empty, SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Pending
        };
        _tracker.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(record);
        _tracker.DeleteAsync(5, Arg.Any<CancellationToken>()).Returns(true);
        var sut = BuildSut();

        var result = await sut.DeleteBookAsync(5);

        Assert.Equal(DeleteBookResult.Deleted, result);
        await _vectorStore.DidNotReceive().DeleteByHashAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _tracker.Received(1).DeleteAsync(5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_NoHash_ComputesAndSetsHash()
    {
        var record = new IngestionRecord
        {
            Id = 22, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = string.Empty, SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Failed
        };
        _tracker.GetByIdAsync(22, Arg.Any<CancellationToken>()).Returns(record);
        _extractor.ExtractPages(_tempFile).Returns([(1, "short text")]);
        var expectedHash = await HashFileAsync(_tempFile);
        var sut = BuildSut();

        await sut.ExtractBookAsync(22);

        await _tracker.Received(1).MarkHashAsync(22, expectedHash, Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkExtractedAsync(22, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_RecordNotFound_Returns()
    {
        _tracker.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);
        var sut = BuildSut();

        await sut.ExtractBookAsync(999);

        await _tracker.Received(1).GetByIdAsync(999, Arg.Any<CancellationToken>());
        _extractor.DidNotReceive().ExtractPages(Arg.Any<string>());
        await _classifier.DidNotReceive().ClassifyPageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_PagesBelowThreshold_SkipsClassification()
    {
        var record = new IngestionRecord
        {
            Id = 20, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = "hash20", SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Completed
        };
        _tracker.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(record);
        // Page text is fewer than 100 characters — below the skip threshold
        _extractor.ExtractPages(_tempFile).Returns([(1, "short text")]);
        var sut = BuildSut();

        await sut.ExtractBookAsync(20);

        await _classifier.DidNotReceive().ClassifyPageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkExtractedAsync(20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_ValidPage_ClassifiesAndExtracts()
    {
        var record = new IngestionRecord
        {
            Id = 21, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = "hash21", SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Completed
        };
        _tracker.GetByIdAsync(21, Arg.Any<CancellationToken>()).Returns(record);

        var pageText = new string('x', 200); // well above 100-char threshold
        _extractor.ExtractPages(_tempFile).Returns([(5, pageText)]);

        _classifier.ClassifyPageAsync(pageText, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["Spell"]));

        var entity = new ExtractedEntity(
            Page: 5,
            SourceBook: "PHB",
            Version: "Edition2014",
            Partial: false,
            Type: "Spell",
            Name: "Fireball",
            Data: new JsonObject { ["description"] = "test" });

        _entityExtractor.ExtractAsync(pageText, "Spell", 5, "PHB", "Edition2014", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExtractedEntity>>([entity]));

        var sut = BuildSut();

        await sut.ExtractBookAsync(21);

        await _classifier.Received(1).ClassifyPageAsync(pageText, Arg.Any<CancellationToken>());
        await _jsonStore.Received(1).SavePageAsync(21, 5, Arg.Any<IReadOnlyList<ExtractedEntity>>(), Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkExtractedAsync(21, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestJsonAsync_RecordNotFound_Returns()
    {
        _tracker.GetByIdAsync(998, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);
        var sut = BuildSut();

        await sut.IngestJsonAsync(998);

        await _tracker.Received(1).GetByIdAsync(998, Arg.Any<CancellationToken>());
        await _jsonPipeline.DidNotReceive().IngestAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestJsonAsync_ValidRecord_RunsPipelineAndMarksIngested()
    {
        var record = new IngestionRecord
        {
            Id = 30, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = "hash30", SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Extracted
        };
        _tracker.GetByIdAsync(30, Arg.Any<CancellationToken>()).Returns(record);
        _jsonStore.LoadAllPagesAsync(30, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<IReadOnlyList<ExtractedEntity>>>(new List<IReadOnlyList<ExtractedEntity>>()));
        var sut = BuildSut();

        await sut.IngestJsonAsync(30);

        await _jsonPipeline.Received(1).IngestAsync(30, "hash30", Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkJsonIngestedAsync(30, 0, Arg.Any<CancellationToken>());
    }
}
