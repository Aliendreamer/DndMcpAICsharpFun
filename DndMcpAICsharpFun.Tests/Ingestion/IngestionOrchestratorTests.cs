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
    private readonly IPdfBookmarkReader _bookmarkReader = Substitute.For<IPdfBookmarkReader>();
    private readonly ITocCategoryClassifier _tocClassifier = Substitute.For<ITocCategoryClassifier>();
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
            _bookmarkReader, _tocClassifier,
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
        _bookmarkReader.ReadBookmarks(_tempFile).Returns([]);
        _tocClassifier.ClassifyAsync(Arg.Any<IReadOnlyList<PdfBookmark>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TocCategoryMap([])));
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
        _bookmarkReader.ReadBookmarks(_tempFile).Returns([]);
        _tocClassifier.ClassifyAsync(Arg.Any<IReadOnlyList<PdfBookmark>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TocCategoryMap([])));
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

        // No bookmarks → empty TocCategoryMap → fall back to classifier
        _bookmarkReader.ReadBookmarks(_tempFile).Returns([]);
        _tocClassifier.ClassifyAsync(Arg.Any<IReadOnlyList<PdfBookmark>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TocCategoryMap([])));

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

    // --- TOC-guided dispatch tests ---

    [Fact]
    public async Task ExtractBookAsync_TocMapEmpty_FallsBackToClassifierForAllPages()
    {
        var record = new IngestionRecord
        {
            Id = 40, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = "hash40", SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Completed
        };
        _tracker.GetByIdAsync(40, Arg.Any<CancellationToken>()).Returns(record);

        var pageText = new string('x', 200);
        _extractor.ExtractPages(_tempFile).Returns([(5, pageText)]);

        // Bookmark reader returns empty list → empty TocCategoryMap
        _bookmarkReader.ReadBookmarks(_tempFile).Returns([]);
        _tocClassifier.ClassifyAsync(Arg.Any<IReadOnlyList<PdfBookmark>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TocCategoryMap([])));

        _classifier.ClassifyPageAsync(pageText, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["Spell"]));

        _entityExtractor.ExtractAsync(pageText, "Spell", 5, "PHB", "Edition2014", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExtractedEntity>>([]));

        var sut = BuildSut();
        await sut.ExtractBookAsync(40);

        // The old classifier path should have been used
        await _classifier.Received(1).ClassifyPageAsync(pageText, Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkExtractedAsync(40, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_TocMapNonEmpty_PageMapsToCategory_RunsOnlyThatExtractor()
    {
        var record = new IngestionRecord
        {
            Id = 41, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = "hash41", SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Completed
        };
        _tracker.GetByIdAsync(41, Arg.Any<CancellationToken>()).Returns(record);

        var pageText = new string('x', 200);
        _extractor.ExtractPages(_tempFile).Returns([(5, pageText)]);

        // Bookmark reader returns one bookmark mapping page 1 → Spell
        var bookmarks = new List<PdfBookmark> { new PdfBookmark("Spells", 1) };
        _bookmarkReader.ReadBookmarks(_tempFile).Returns(bookmarks);

        var tocMap = new TocCategoryMap([(1, ContentCategory.Spell)]);
        _tocClassifier.ClassifyAsync(Arg.Any<IReadOnlyList<PdfBookmark>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(tocMap));

        var entity = new ExtractedEntity(
            Page: 5, SourceBook: "PHB", Version: "Edition2014",
            Partial: false, Type: "Spell", Name: "Fireball",
            Data: new JsonObject { ["description"] = "test" });

        _entityExtractor.ExtractAsync(pageText, "Spell", 5, "PHB", "Edition2014", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExtractedEntity>>([entity]));

        var sut = BuildSut();
        await sut.ExtractBookAsync(41);

        // Only the entityExtractor should be called, NOT the old classifier
        await _classifier.DidNotReceive().ClassifyPageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _entityExtractor.Received(1).ExtractAsync(pageText, "Spell", 5, "PHB", "Edition2014", Arg.Any<CancellationToken>());
        await _jsonStore.Received(1).SavePageAsync(41, 5, Arg.Any<IReadOnlyList<ExtractedEntity>>(), Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkExtractedAsync(41, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_TocMapNonEmpty_PageMapsToNull_SkipsPage()
    {
        var record = new IngestionRecord
        {
            Id = 42, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = "hash42", SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Completed
        };
        _tracker.GetByIdAsync(42, Arg.Any<CancellationToken>()).Returns(record);

        var pageText = new string('x', 200);
        _extractor.ExtractPages(_tempFile).Returns([(5, pageText)]);

        // TocCategoryMap with null category for all pages
        var bookmarks = new List<PdfBookmark> { new PdfBookmark("Introduction", 1) };
        _bookmarkReader.ReadBookmarks(_tempFile).Returns(bookmarks);

        // Map page 1+ → null (not a D&D content category)
        var tocMap = new TocCategoryMap([(1, (ContentCategory?)null)]);
        _tocClassifier.ClassifyAsync(Arg.Any<IReadOnlyList<PdfBookmark>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(tocMap));

        var sut = BuildSut();
        await sut.ExtractBookAsync(42);

        // No extractor or classifier calls since page is skipped
        await _classifier.DidNotReceive().ClassifyPageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _entityExtractor.DidNotReceive().ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _jsonStore.DidNotReceive().SavePageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IReadOnlyList<ExtractedEntity>>(), Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkExtractedAsync(42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_Cancelled_CleansUpAndRethrows()
    {
        var record = new IngestionRecord
        {
            Id = 43, FilePath = _tempFile, FileName = "test.pdf",
            FileHash = "hash43", SourceName = "PHB", Version = "Edition2014",
            DisplayName = "PHB", Status = IngestionStatus.Completed
        };
        _tracker.GetByIdAsync(43, Arg.Any<CancellationToken>()).Returns(record);

        var pageText = new string('x', 200);
        _extractor.ExtractPages(_tempFile).Returns([(5, pageText)]);

        _bookmarkReader.ReadBookmarks(_tempFile).Returns([]);
        _tocClassifier.ClassifyAsync(Arg.Any<IReadOnlyList<PdfBookmark>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TocCategoryMap([])));

        // Classifier throws OperationCanceledException to simulate cancellation
        _classifier.ClassifyPageAsync(pageText, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<string>>>(_ => throw new OperationCanceledException());

        var sut = BuildSut();

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.ExtractBookAsync(43));

        _jsonStore.Received(1).DeleteAllPages(43);
        await _tracker.Received(1).ResetForReingestionAsync(43, CancellationToken.None);
    }
}
