using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Ingestion;
using DndMcpAICsharpFun.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Programmatic;

public class ProgrammaticExtractionRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "prun-" + Guid.NewGuid().ToString("N"));
    private readonly IIngestionTracker _tracker = Substitute.For<IIngestionTracker>();
    private readonly IBookExtractorClient _client = Substitute.For<IBookExtractorClient>();
    private readonly IEntityIngestionOrchestrator _ingestion = Substitute.For<IEntityIngestionOrchestrator>();
    private readonly RecordingEntityVectorStore _store = new();
    private readonly CanonicalBookLocks _locks = new();

    public ProgrammaticExtractionRunnerTests()
    {
        Directory.CreateDirectory(_dir);
        _tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new IngestionRecord
        {
            Id = 1,
            FilePath = "/books/mm.pdf",
            FileName = "mm.pdf",
            FileHash = "hash1",
            Version = "Edition2014",
            DisplayName = "Monster Manual",
            FivetoolsSourceKey = "MM",
        });
        _client.SubmitAsync("mm.pdf", "mm14", Arg.Any<CancellationToken>()).Returns("job1");
        _client.GetStatusAsync("job1", Arg.Any<CancellationToken>())
            .Returns(new ExtractorJobStatus("done", 2, 2, null));
        _client.GetResultAsync("job1", Arg.Any<CancellationToken>()).Returns(Result());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static ExtractedEntity Ent(string id, string name, bool pass) => new(
        id, EntityType.Monster, name, 10, name + " text", J("""{"cr":"1"}"""),
        [new EntityCheck("c", pass, pass ? "" : "bad")],
        new EntityProvenance([new ProvenancePage(10, [new PageBox(1, 2, 3, 4)])]));

    private static ExtractorResult Result() => new(
        [Ent("mm14.monster.goblin", "Goblin", true), Ent("mm14.monster.orc", "Orc", false)],
        J("""{"pages":2}"""));

    private ProgrammaticExtractionRunner Runner(TimeSpan? timeout = null, Action<string>? deleteFile = null) => new(
        _tracker, _client, new OverridesStore(Options.Create(new EntityIngestionOptions { CanonicalDirectory = _dir })),
        _ingestion, new BookSourceRegistry("does-not-exist.json"),
        Options.Create(new EntityIngestionOptions { CanonicalDirectory = _dir }),
        Options.Create(new IngestionOptions { BooksPath = "/books" }),
        Options.Create(new BookExtractorOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(5),
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        }),
        new CanonicalJsonWriter(), _store, _locks, NullLogger<ProgrammaticExtractionRunner>.Instance, deleteFile);

    private string P(string name) => Path.Combine(_dir, name);

    private void SeedOld()
    {
        File.WriteAllText(P("mm14.json"), "OLD");
        File.WriteAllText(P("mm14.errors.json"), "E");
        File.WriteAllText(P("mm14.warnings.json"), "W");
        File.WriteAllText(P("mm14.declined.json"), "D");
        File.WriteAllText(P("mm14.progress.json"), "P");
        File.WriteAllText(P("mm14.progress.errors.json"), "PE");
    }

    [Fact]
    public async Task Success_replaces_files_and_sidecars()
    {
        SeedOld();

        var r = await Runner().RunAsync(1, CancellationToken.None);

        r.Entities.Should().Be(2);
        r.Accepted.Should().Be(1);
        r.NeedsReview.Should().Be(1);
        foreach (var s in new[] { "errors", "warnings", "declined", "progress", "progress.errors" })
            File.Exists(P($"mm14.{s}.json")).Should().BeFalse(s);
        File.Exists(P("mm14.report.json")).Should().BeTrue();
        var doc = JsonDocument.Parse(File.ReadAllText(P("mm14.json"))).RootElement;
        doc.GetProperty("schemaVersion").GetString().Should().Be("2");
        doc.GetProperty("book").GetProperty("sourceBook").GetString().Should().Be("MM");
        doc.GetProperty("book").GetProperty("edition").GetString().Should().Be("Edition2014");
        doc.GetProperty("book").GetProperty("fileHash").GetString().Should().Be("hash1");
        var ents = doc.GetProperty("entities").EnumerateArray().ToList();
        ents.Select(e => e.GetProperty("disposition").GetString())
            .Should().Equal("Accepted", "NeedsReview");
        ents[0].GetProperty("dataSource").GetString().Should().Be("programmatic");
        ents[0].GetProperty("authority").GetString().Should().Be("book");
        ents[0].GetProperty("provenance").GetProperty("pages")[0].GetProperty("boxes")[0]
            .GetProperty("x1").GetDouble().Should().Be(3);
        await _ingestion.Received(1).IngestEntitiesAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failure_keeps_old_files()
    {
        SeedOld();
        _client.GetResultAsync("job1", Arg.Any<CancellationToken>())
            .Returns<ExtractorResult>(_ => throw new HttpRequestException("boom"));

        var act = () => Runner().RunAsync(1, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        File.ReadAllText(P("mm14.json")).Should().Be("OLD");
        File.ReadAllText(P("mm14.errors.json")).Should().Be("E");
        File.ReadAllText(P("mm14.declined.json")).Should().Be("D");
        File.Exists(P("mm14.report.json")).Should().BeFalse();
        await _ingestion.DidNotReceiveWithAnyArgs().IngestEntitiesAsync(default, default);
    }

    [Fact]
    public async Task Failed_job_keeps_old_files()
    {
        SeedOld();
        _client.GetStatusAsync("job1", Arg.Any<CancellationToken>())
            .Returns(new ExtractorJobStatus("failed", 0, 2, "ocr crashed"));

        var act = () => Runner().RunAsync(1, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ocr crashed*");
        File.ReadAllText(P("mm14.json")).Should().Be("OLD");
        await _ingestion.DidNotReceiveWithAnyArgs().IngestEntitiesAsync(default, default);
    }

    [Fact]
    public async Task Timeout_keeps_old_files()
    {
        SeedOld();
        _client.GetStatusAsync("job1", Arg.Any<CancellationToken>())
            .Returns(new ExtractorJobStatus("running", 1, 2, null));

        var act = () => Runner(TimeSpan.FromMilliseconds(60)).RunAsync(1, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        File.ReadAllText(P("mm14.json")).Should().Be("OLD");
        File.ReadAllText(P("mm14.errors.json")).Should().Be("E");
        await _ingestion.DidNotReceiveWithAnyArgs().IngestEntitiesAsync(default, default);
    }

    [Fact]
    public async Task Overrides_applied_before_write()
    {
        var store = new OverridesStore(Options.Create(new EntityIngestionOptions { CanonicalDirectory = _dir }));
        await store.SaveAsync("mm14", new EntityOverrides(
            [new FieldOverride("mm14.monster.goblin", "fields.cr", J("\"2\""), J("\"1\""), "fix", DateTimeOffset.UnixEpoch)],
            [new Approval("mm14.monster.orc", "ok", DateTimeOffset.UnixEpoch, ["c"])]), CancellationToken.None);

        var r = await Runner().RunAsync(1, CancellationToken.None);

        r.Accepted.Should().Be(2);
        r.NeedsReview.Should().Be(0);
        var ents = JsonDocument.Parse(File.ReadAllText(P("mm14.json"))).RootElement
            .GetProperty("entities").EnumerateArray().ToList();
        ents[0].GetProperty("fields").GetProperty("cr").GetString().Should().Be("2");
        File.Exists(P("mm14.overrides.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Orphaned_overrides_reported()
    {
        var store = new OverridesStore(Options.Create(new EntityIngestionOptions { CanonicalDirectory = _dir }));
        await store.SaveAsync("mm14", new EntityOverrides([], [new Approval("mm14.monster.gone", "x", DateTimeOffset.UnixEpoch)]),
            CancellationToken.None);

        var r = await Runner().RunAsync(1, CancellationToken.None);

        r.Orphans.Should().Equal("mm14.monster.gone");
    }

    [Fact]
    public async Task Ingestion_failure_after_swap_keeps_new_file_and_says_to_retry_ingest()
    {
        SeedOld();
        _ingestion.IngestEntitiesAsync(1, Arg.Any<CancellationToken>())
            .Returns<Task<EntityIngestionResult>>(_ => throw new InvalidOperationException("qdrant down"));

        var act = () => Runner().RunAsync(1, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<PostSwapIngestionException>()).Which;
        ex.Message.Should().Contain("ingest-entities").And.Contain("new canonical file is in place");
        ex.InnerException!.Message.Should().Be("qdrant down");
        File.ReadAllText(P("mm14.json")).Should().NotBe("OLD");
        File.Exists(P("mm14.errors.json")).Should().BeFalse();
        File.Exists(P("mm14.report.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Sidecar_delete_failure_is_a_post_swap_error_not_untouched()
    {
        SeedOld();

        var act = () => Runner(deleteFile: _ => throw new IOException("locked")).RunAsync(1, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<PartialSwapException>()).Which;
        ex.Should().BeAssignableTo<PostSwapException>();
        ex.Message.Should().Contain("extract-programmatic").And.NotContain("untouched");
        File.ReadAllText(P("mm14.json")).Should().NotBe("OLD");
        await _ingestion.DidNotReceiveWithAnyArgs().IngestEntitiesAsync(default, default);
    }

    [Fact]
    public async Task Report_write_failure_leaves_files_untouched_and_no_tmp()
    {
        SeedOld();
        Directory.CreateDirectory(P("mm14.report.json")); // moving the report onto a directory fails

        var act = () => Runner().RunAsync(1, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        thrown.Should().NotBeAssignableTo<PostSwapException>();
        File.ReadAllText(P("mm14.json")).Should().Be("OLD");
        File.Exists(P("mm14.report.json.tmp")).Should().BeFalse();
    }

    [Fact]
    public async Task Legacy_approval_without_waived_checks_waives_nothing()
    {
        var store = new OverridesStore(Options.Create(new EntityIngestionOptions { CanonicalDirectory = _dir }));
        await store.SaveAsync("mm14", new EntityOverrides([],
            [new Approval("mm14.monster.orc", "ok", DateTimeOffset.UnixEpoch)]), CancellationToken.None);

        var r = await Runner().RunAsync(1, CancellationToken.None);

        r.Accepted.Should().Be(1);
        r.NeedsReview.Should().Be(1);
    }

    [Fact]
    public async Task Entity_without_checks_fails_closed_as_checks_missing()
    {
        _client.GetResultAsync("job1", Arg.Any<CancellationToken>()).Returns(new ExtractorResult(
            [new ExtractedEntity("mm14.monster.imp", EntityType.Monster, "Imp", 10, "Imp text", J("{}"), [])],
            J("{}")));

        var r = await Runner().RunAsync(1, CancellationToken.None);

        r.Accepted.Should().Be(0);
        r.NeedsReview.Should().Be(1);
        var ent = JsonDocument.Parse(File.ReadAllText(P("mm14.json"))).RootElement.GetProperty("entities")[0];
        ent.GetProperty("disposition").GetString().Should().Be("NeedsReview");
        var check = ent.GetProperty("checks")[0];
        check.GetProperty("name").GetString().Should().Be(ProgrammaticExtractionRunner.ChecksMissing);
        check.GetProperty("passed").GetBoolean().Should().BeFalse();
        check.GetProperty("reason").GetString().Should().Be("extractor returned no checks");
    }

    [Fact]
    public async Task Success_purges_prior_5etools_import_points_for_the_book()
    {
        static EntityPoint Pt(string id, string hash) => new(new EntityEnvelope(id, EntityType.Monster, id, hash.EndsWith("PHB") ? "PHB" : "MM",
            "Edition2014", null, new FirstAppearance("MM", "Edition2014"), [], [], "", default,
            hash == "hash1" ? "programmatic" : "5etools"), new float[4], hash);
        await _store.UpsertAsync([
            Pt("mm14.monster.orc", "5etools:MM"),     // flagged programmatic entity: stale 5etools point must go
            Pt("mm14.monster.kobold", "5etools:MM"),
            Pt("phb14.spell.fireball", "5etools:PHB"), // another book: untouched
            Pt("mm14.monster.goblin", "hash1"),       // programmatic point: untouched
        ]);

        await Runner().RunAsync(1, CancellationToken.None);

        _store.Ids.Should().BeEquivalentTo(["phb14.spell.fireball", "mm14.monster.goblin"]);
    }

    private static EntityPoint StalePt(string id, string book, string hash, string dataSource) => new(
        new EntityEnvelope(id, EntityType.Monster, id, book, "Edition2014", null,
            new FirstAppearance(book, "Edition2014"), [], [], "", default, dataSource), new float[4], hash);

    [Fact]
    public async Task Success_purges_stale_non_programmatic_points_of_the_book_by_source_key()
    {
        await _store.UpsertAsync([
            StalePt("mm14.monster.old-llm", "MM", "", "llm"),                  // empty hash, old extraction
            StalePt("mm14.monster.other-hash", "MM", "oldhash", "programmatic"), // different hash
            StalePt("mm14.monster.goblin", "MM", "hash1", "programmatic"),      // this run's point: kept
            StalePt("phb14.spell.fireball", "PHB", "", "llm"),                  // other book: kept
        ]);

        await Runner().RunAsync(1, CancellationToken.None);

        _store.DeleteStaleForBookCalls.Should().Equal(("MM", "hash1"));
        _store.Ids.Should().BeEquivalentTo(["mm14.monster.goblin", "phb14.spell.fireball"]);
    }

    [Fact]
    public async Task Ingestion_failure_does_not_purge_stale_points()
    {
        _ingestion.IngestEntitiesAsync(1, Arg.Any<CancellationToken>())
            .Returns<Task<EntityIngestionResult>>(_ => throw new InvalidOperationException("qdrant down"));
        await _store.UpsertAsync([StalePt("mm14.monster.old-llm", "MM", "", "llm")]);

        var act = () => Runner().RunAsync(1, CancellationToken.None);

        await act.Should().ThrowAsync<PostSwapIngestionException>();
        _store.DeleteStaleForBookCalls.Should().BeEmpty();
        _store.Ids.Should().Contain("mm14.monster.old-llm");
    }

    [Fact]
    public async Task Swap_waits_for_the_shared_book_lock()
    {
        SeedOld();
        var held = await _locks.AcquireAsync("mm14", CancellationToken.None);

        var run = Runner().RunAsync(1, CancellationToken.None);
        await Task.Delay(200);

        run.IsCompleted.Should().BeFalse();
        File.ReadAllText(P("mm14.json")).Should().Be("OLD");
        held.Dispose();
        await run;
        File.ReadAllText(P("mm14.json")).Should().NotBe("OLD");
    }
}
