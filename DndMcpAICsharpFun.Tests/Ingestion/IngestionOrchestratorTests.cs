using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class IngestionOrchestratorTests : IDisposable
{
    private readonly IIngestionTracker _tracker = Substitute.For<IIngestionTracker>();
    private readonly IPdfStructuredExtractor _extractor = Substitute.For<IPdfStructuredExtractor>();
    private readonly IVectorStoreService _vectorStore = Substitute.For<IVectorStoreService>();
    private readonly ILlmEntityExtractor _entityExtractor = Substitute.For<ILlmEntityExtractor>();
    private readonly IEntityJsonStore _jsonStore = Substitute.For<IEntityJsonStore>();
    private readonly IJsonIngestionPipeline _jsonPipeline = Substitute.For<IJsonIngestionPipeline>();
    private readonly ITocMapExtractor _tocMapExtractor = Substitute.For<ITocMapExtractor>();
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
        var opts = Options.Create(new IngestionOptions { MaxChunkTokens = 512, OverlapTokens = 64 });
        return new IngestionOrchestrator(
            _tracker, _extractor, _vectorStore,
            _entityExtractor, _jsonStore, _jsonPipeline, opts,
            _tocMapExtractor,
            NullLogger<IngestionOrchestrator>.Instance);
    }

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, CancellationToken.None);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private IngestionRecord MakeRecord(
        int id = 1,
        IngestionStatus status = IngestionStatus.Pending,
        int? tocPage = 3) => new()
    {
        Id = id,
        FilePath = _tempFile,
        FileName = "test.pdf",
        FileHash = string.Empty,
        SourceName = "PHB",
        Version = "Edition2014",
        DisplayName = "PHB",
        Status = status,
        TocPage = tocPage,
    };

    // ── Delete ──────────────────────────────────────────────────────────────

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
        _tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(1, IngestionStatus.Processing));
        var sut = BuildSut();

        var result = await sut.DeleteBookAsync(1);

        Assert.Equal(DeleteBookResult.Conflict, result);
        await _vectorStore.DidNotReceive().DeleteByHashAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteBookAsync_JsonIngestedRecord_DeletesVectorsFileSqlite()
    {
        var record = MakeRecord(2, IngestionStatus.JsonIngested);
        record.FileHash = "abc123";
        record.ChunkCount = 10;
        _tracker.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(record);
        _tracker.DeleteAsync(2, Arg.Any<CancellationToken>()).Returns(true);
        var sut = BuildSut();

        var result = await sut.DeleteBookAsync(2);

        Assert.Equal(DeleteBookResult.Deleted, result);
        await _vectorStore.Received(1).DeleteByHashAsync("abc123", 10, Arg.Any<CancellationToken>());
        _jsonStore.Received(1).DeleteAllPages(2);
        await _tracker.Received(1).DeleteAsync(2, Arg.Any<CancellationToken>());
        Assert.False(File.Exists(_tempFile));
    }

    [Fact]
    public async Task DeleteBookAsync_PendingRecord_SkipsVectorsDeletesSqlite()
    {
        _tracker.GetByIdAsync(5, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(5, IngestionStatus.Pending));
        _tracker.DeleteAsync(5, Arg.Any<CancellationToken>()).Returns(true);
        var sut = BuildSut();

        var result = await sut.DeleteBookAsync(5);

        Assert.Equal(DeleteBookResult.Deleted, result);
        await _vectorStore.DidNotReceive().DeleteByHashAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _tracker.Received(1).DeleteAsync(5, Arg.Any<CancellationToken>());
    }

    // ── Extract ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractBookAsync_RecordNotFound_Returns()
    {
        _tracker.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);
        var sut = BuildSut();

        await sut.ExtractBookAsync(999);

        await _tracker.Received(1).GetByIdAsync(999, Arg.Any<CancellationToken>());
        _extractor.DidNotReceive().ExtractPages(Arg.Any<string>());
    }

    [Fact]
    public async Task ExtractBookAsync_TocPageNull_MarksFailedWithoutExtraction()
    {
        _tracker.GetByIdAsync(10, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(10, tocPage: null));
        var sut = BuildSut();

        await sut.ExtractBookAsync(10);

        _extractor.DidNotReceive().ExtractPages(Arg.Any<string>());
        await _tracker.Received(1).MarkFailedAsync(10, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_NoHash_ComputesAndSetsHash()
    {
        var record = MakeRecord(22, tocPage: 1);
        _tracker.GetByIdAsync(22, Arg.Any<CancellationToken>()).Returns(record);

        var tocPage = new StructuredPage(1, "Table of Contents", [new PageBlock(1, "body", "TOC text")]);
        _extractor.ExtractSinglePage(_tempFile, 1).Returns(tocPage);
        _extractor.ExtractPages(_tempFile).Returns([
            new StructuredPage(1, "short text", [new PageBlock(1, "body", "short text")])
        ]);
        _tocMapExtractor.ExtractMapAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TocSectionEntry>>([]));

        var expectedHash = await HashFileAsync(_tempFile);
        var sut = BuildSut();

        await sut.ExtractBookAsync(22);

        await _tracker.Received(1).MarkHashAsync(22, expectedHash, Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkExtractedAsync(22, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_PagesBelowThreshold_SkipsExtraction()
    {
        var record = MakeRecord(20, tocPage: 1);
        _tracker.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(record);

        var tocStructuredPage = new StructuredPage(1, "TOC text", [new PageBlock(1, "body", "TOC")]);
        _extractor.ExtractSinglePage(_tempFile, 1).Returns(tocStructuredPage);
        _extractor.ExtractPages(_tempFile).Returns([
            new StructuredPage(2, "short", [new PageBlock(1, "body", "short")])
        ]);
        _tocMapExtractor.ExtractMapAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TocSectionEntry>>([
                new TocSectionEntry("Spells", ContentCategory.Spell, 1, 50)
            ]));

        var sut = BuildSut();

        await sut.ExtractBookAsync(20);

        await _entityExtractor.DidNotReceive().ExtractAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkExtractedAsync(20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_TocGuidedPage_CallsExtractorWithSectionContext()
    {
        var record = MakeRecord(21, tocPage: 1);
        _tracker.GetByIdAsync(21, Arg.Any<CancellationToken>()).Returns(record);

        var tocStructuredPage = new StructuredPage(1, "TOC", [new PageBlock(1, "body", "TOC")]);
        _extractor.ExtractSinglePage(_tempFile, 1).Returns(tocStructuredPage);

        var pageText = new string('x', 200);
        _extractor.ExtractPages(_tempFile).Returns([
            new StructuredPage(45, pageText, [new PageBlock(1, "h2", "Wizard")])
        ]);

        _tocMapExtractor.ExtractMapAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TocSectionEntry>>([
                new TocSectionEntry("Wizard", ContentCategory.Class, 45, 80)
            ]));

        var entity = new ExtractedEntity(
            Page: 45, SourceBook: "PHB", Version: "Edition2014",
            Partial: false, Type: "Class", Name: "Wizard",
            Data: new JsonObject { ["description"] = "test" });

        _entityExtractor.ExtractAsync(
            Arg.Any<string>(), "Class", 45, "PHB", "Edition2014",
            "Wizard", 45, 80, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExtractedEntity>>([entity]));

        var sut = BuildSut();

        await sut.ExtractBookAsync(21);

        await _entityExtractor.Received(1).ExtractAsync(
            Arg.Any<string>(), "Class", 45, "PHB", "Edition2014",
            "Wizard", 45, 80, Arg.Any<CancellationToken>());
        await _jsonStore.Received(1).SavePageAsync(
            21, Arg.Any<StructuredPage>(), Arg.Any<IReadOnlyList<ExtractedEntity>>(),
            Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkExtractedAsync(21, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_PageOutsideTocMap_SkipsExtraction()
    {
        var record = MakeRecord(25, tocPage: 1);
        _tracker.GetByIdAsync(25, Arg.Any<CancellationToken>()).Returns(record);

        var tocStructuredPage = new StructuredPage(1, "TOC", [new PageBlock(1, "body", "TOC")]);
        _extractor.ExtractSinglePage(_tempFile, 1).Returns(tocStructuredPage);

        var pageText = new string('x', 200);
        _extractor.ExtractPages(_tempFile).Returns([
            new StructuredPage(999, pageText, [new PageBlock(1, "body", pageText)])
        ]);

        _tocMapExtractor.ExtractMapAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TocSectionEntry>>([
                new TocSectionEntry("Spells", ContentCategory.Spell, 45, 80)
            ]));

        var sut = BuildSut();

        await sut.ExtractBookAsync(25);

        await _entityExtractor.DidNotReceive().ExtractAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    // ── IngestJson ───────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestJsonAsync_RecordNotFound_Returns()
    {
        _tracker.GetByIdAsync(998, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);
        var sut = BuildSut();

        await sut.IngestJsonAsync(998);

        await _jsonPipeline.DidNotReceive().IngestAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestJsonAsync_ValidRecord_RunsPipelineAndMarksIngested()
    {
        var record = MakeRecord(30, IngestionStatus.Extracted);
        record.FileHash = "hash30";
        _tracker.GetByIdAsync(30, Arg.Any<CancellationToken>()).Returns(record);
        _jsonStore.LoadAllPagesAsync(30, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PageData>>([]));
        var sut = BuildSut();

        await sut.IngestJsonAsync(30);

        await _jsonPipeline.Received(1).IngestAsync(30, "hash30", Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkJsonIngestedAsync(30, 0, Arg.Any<CancellationToken>());
    }
}
