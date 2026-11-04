using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Admin.Review;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Ingestion;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Admin;

public sealed class EntityReviewServiceTests : IDisposable
{
    private const string Slug = "t14-book";
    private const string Goblin = "t14-book.monster.goblin";
    private const string Orc = "t14-book.monster.orc";
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "review-" + Guid.NewGuid().ToString("N"));
    private readonly IIngestionTracker _tracker = Substitute.For<IIngestionTracker>();
    private readonly IEntityIngestionOrchestrator _ingestion = Substitute.For<IEntityIngestionOrchestrator>();
    private readonly IBookExtractorClient _client = Substitute.For<IBookExtractorClient>();
    private readonly CanonicalJsonLoader _loader = new();
    private readonly CanonicalJsonWriter _writer = new();

    public EntityReviewServiceTests()
    {
        Directory.CreateDirectory(_dir);
        var record = new IngestionRecord
        {
            Id = 1,
            DisplayName = Slug,
            FilePath = "/books/t14.pdf",
            FileName = "t14.pdf",
            FileHash = "h",
            Version = "Edition2014",
        };
        _tracker.GetAllAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<IngestionRecord> { record });
        _tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static EntityEnvelope Ent(string id, string name, IReadOnlyList<EntityCheck>? checks = null,
        EntityDisposition disp = EntityDisposition.Accepted, string cr = "\"1\"") => new(
        id, EntityType.Monster, name, "T14", "Edition2014", 7,
        new FirstAppearance("T14", "Edition2014", 7), [], [], name + " text",
        J($$"""{"cr":{{cr}},"ac":[{"ac":12}]}"""),
        DataSource: DataSources.Programmatic, Authority: "book",
        NeedsReview: disp != EntityDisposition.Accepted, Disposition: disp, Checks: checks!,
        Provenance: new EntityProvenance([new ProvenancePage(7, [new PageBox(10, 20, 110, 220)])]));

    private static EntityCheck Failed(string reason = "hp mismatch") => new("hp-sane", false, reason);

    private async Task Seed(string schema = "2", params EntityEnvelope[] entities)
    {
        var file = new CanonicalJsonFile(schema, new CanonicalBookMetadata("T14", "Edition2014", "h", Slug), entities);
        await _writer.WriteAsync(Path.Combine(_dir, Slug + ".json"), file, CancellationToken.None);
    }

    private Task SeedDefault() => Seed("2",
        Ent(Goblin, "Goblin"),
        Ent(Orc, "Orc", [Failed()], EntityDisposition.NeedsReview));

    private EntityReviewService Svc()
    {
        var eo = Options.Create(new EntityIngestionOptions { CanonicalDirectory = _dir });
        return new EntityReviewService(_loader, _writer, new OverridesStore(eo), _ingestion, _client, _tracker,
            eo, Options.Create(new IngestionOptions { BooksPath = "/books" }));
    }

    private OverridesStore Store() =>
        new(Options.Create(new EntityIngestionOptions { CanonicalDirectory = _dir }));

    private async Task<EntityEnvelope> FromFile(string id)
    {
        var f = await _loader.LoadAsync(Path.Combine(_dir, Slug + ".json"), CancellationToken.None);
        return f.Entities.Single(e => e.Id == id);
    }

    [Fact]
    public async Task List_flagged_only()
    {
        await SeedDefault();

        var all = await Svc().ListAsync(null, false, CancellationToken.None);
        var flagged = await Svc().ListAsync(Slug, true, CancellationToken.None);

        all.Should().HaveCount(2);
        flagged.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = Orc, Name = "Orc", BookSlug = Slug, Page = 7, Flagged = true, FailedReasons = new[] { "hp mismatch" },
        });
    }

    [Fact]
    public async Task List_ignores_non_programmatic_files()
    {
        await Seed("1", Ent("old.monster.x", "X", [Failed()], EntityDisposition.NeedsReview));

        (await Svc().ListAsync(null, false, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_makes_entity_accepted_and_reindexes()
    {
        await SeedDefault();

        await Svc().ApproveAsync(Orc, "reviewed against page", CancellationToken.None);

        var e = await FromFile(Orc);
        e.Disposition.Should().Be(EntityDisposition.Accepted);
        e.NeedsReview.Should().BeFalse();
        (await Store().LoadAsync(Slug, CancellationToken.None)).Approvals
            .Should().ContainSingle(a => a.EntityId == Orc && a.Reason == "reviewed against page");
        await _ingestion.Received(1).ReindexEntityAsync(1, Orc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Override_on_accepted_entity_updates_file_and_point()
    {
        await SeedDefault();

        await Svc().OverrideAsync(Goblin, "fields.cr", J("\"2\""), "misread digit", CancellationToken.None);

        (await FromFile(Goblin)).Fields.GetProperty("cr").GetString().Should().Be("2");
        var ov = (await Store().LoadAsync(Slug, CancellationToken.None)).Overrides.Should().ContainSingle().Subject;
        ov.Field.Should().Be("fields.cr");
        ov.Extracted!.Value.GetString().Should().Be("1", "the extractor's value is the baseline");
        await _ingestion.Received(1).ReindexEntityAsync(1, Goblin, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Override_invalid_path_throws()
    {
        await SeedDefault();

        var act = () => Svc().OverrideAsync(Goblin, "fields.nope.x", J("1"), "r", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        await _ingestion.DidNotReceiveWithAnyArgs().ReindexEntityAsync(default, default!, default);
    }

    [Fact]
    public async Task Override_empty_path_segment_throws()
    {
        await SeedDefault();

        var act = () => Svc().OverrideAsync(Goblin, "fields.", J("1"), "r", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Override_empty_reason_throws(string reason)
    {
        await SeedDefault();

        var act = () => Svc().OverrideAsync(Goblin, "fields.cr", J("\"2\""), reason, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        File.Exists(Path.Combine(_dir, Slug + ".overrides.json")).Should().BeFalse();
    }

    [Fact]
    public async Task Approve_empty_reason_throws()
    {
        await SeedDefault();

        var act = () => Svc().ApproveAsync(Orc, " ", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Approve_rebases_override_baseline_and_clears_drift()
    {
        // The file holds an entity whose override (cr 3) was applied over a re-extraction that now reads "5".
        var driftReason = "extraction changed under your override at fields.cr: was \"2\", now \"5\"";
        await Seed("2", Ent(Orc, "Orc", [new EntityCheck(OverrideMerger.DriftCheck, false, driftReason)],
            EntityDisposition.NeedsReview, cr: "\"3\""));
        await Store().SaveAsync(Slug, new EntityOverrides(
            [new FieldOverride(Orc, "fields.cr", J("\"3\""), J("\"2\""), "fix", At)], []), CancellationToken.None);

        await Svc().ApproveAsync(Orc, "yes, mine is right", CancellationToken.None);

        var saved = await Store().LoadAsync(Slug, CancellationToken.None);
        saved.Overrides.Single().Extracted!.Value.GetString().Should().Be("5");
        var e = await FromFile(Orc);
        e.Checks.Should().NotContain(c => c.Name == OverrideMerger.DriftCheck);
        e.Disposition.Should().Be(EntityDisposition.Accepted);
        e.Fields.GetProperty("cr").GetString().Should().Be("3");

        // A later re-extraction that reads "5" again must not drift any more.
        var again = OverrideMerger.Apply([Ent(Orc, "Orc", cr: "\"5\"")], saved).Entities[0];
        again.Checks.Should().BeEmpty();
    }

    [Fact]
    public async Task Resaving_an_override_clears_drift()
    {
        var driftReason = "extraction changed under your override at fields.cr: was \"2\", now \"5\"";
        await Seed("2", Ent(Orc, "Orc", [new EntityCheck(OverrideMerger.DriftCheck, false, driftReason)],
            EntityDisposition.NeedsReview, cr: "\"3\""));
        await Store().SaveAsync(Slug, new EntityOverrides(
            [new FieldOverride(Orc, "fields.cr", J("\"3\""), J("\"2\""), "fix", At)], []), CancellationToken.None);

        await Svc().OverrideAsync(Orc, "fields.cr", J("\"4\""), "still wrong", CancellationToken.None);

        var saved = await Store().LoadAsync(Slug, CancellationToken.None);
        var ov = saved.Overrides.Should().ContainSingle().Subject;
        ov.Value.GetString().Should().Be("4");
        ov.Extracted!.Value.GetString().Should().Be("5");
        var e = await FromFile(Orc);
        e.Checks.Should().BeEmpty();
        e.Disposition.Should().Be(EntityDisposition.Accepted);
        e.Fields.GetProperty("cr").GetString().Should().Be("4");
    }

    [Fact]
    public async Task Remove_unapplied_override_unblocks_entity()
    {
        await Seed("2", Ent(Orc, "Orc",
            [new EntityCheck(OverrideMerger.UnappliedCheck, false, "override at fields.gone.x could not be applied: nope")],
            EntityDisposition.NeedsReview));
        await Store().SaveAsync(Slug, new EntityOverrides(
            [new FieldOverride(Orc, "fields.gone.x", J("1"), null, "fix", At)],
            [new Approval(Orc, "ok", At)]), CancellationToken.None);

        await Svc().RemoveOverrideAsync(Orc, "fields.gone.x", CancellationToken.None);

        (await Store().LoadAsync(Slug, CancellationToken.None)).Overrides.Should().BeEmpty();
        var e = await FromFile(Orc);
        e.Checks.Should().BeEmpty();
        e.Disposition.Should().Be(EntityDisposition.Accepted);
        await _ingestion.Received(1).ReindexEntityAsync(1, Orc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_applied_override_restores_extracted_value()
    {
        await Seed("2", Ent(Goblin, "Goblin", cr: "\"3\""));
        await Store().SaveAsync(Slug, new EntityOverrides(
            [new FieldOverride(Goblin, "fields.cr", J("\"3\""), J("\"1\""), "fix", At)], []), CancellationToken.None);

        await Svc().RemoveOverrideAsync(Goblin, "fields.cr", CancellationToken.None);

        (await FromFile(Goblin)).Fields.GetProperty("cr").GetString().Should().Be("1");
        (await Store().LoadAsync(Slug, CancellationToken.None)).Overrides.Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_unknown_override_throws()
    {
        await SeedDefault();

        var act = () => Svc().RemoveOverrideAsync(Goblin, "fields.cr", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void VisibleChecks_include_passed_repairs_and_failures_but_not_silent_passes()
    {
        var e = Ent(Goblin, "Goblin",
        [
            new EntityCheck("ability-repaired", true, "STR 1O read as 10"),
            new EntityCheck("name-repaired", true, "Gob1in -> Goblin"),
            new EntityCheck("hp-sane", false, "hp mismatch"),
            new EntityCheck("silent", true, ""),
        ]);

        EntityReviewService.VisibleChecks(e).Select(c => c.Name)
            .Should().Equal("ability-repaired", "name-repaired", "hp-sane");
    }

    [Fact]
    public async Task PageImage_returns_data_uri_for_a_provenance_page()
    {
        await SeedDefault();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
        _client.GetPageImageAsync("t14.pdf", 7, Arg.Any<CancellationToken>()).Returns(png);

        var uri = await Svc().GetPageImageDataUriAsync(Orc, 7, CancellationToken.None);

        uri.Should().Be("data:image/png;base64," + Convert.ToBase64String(png));
    }

    [Fact]
    public void TryGetPngSize_reads_the_ihdr()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52,
            0, 0, 6, 0x72, 0, 0, 8, 0xCA, 8, 6, 0, 0, 0];
        var uri = "data:image/png;base64," + Convert.ToBase64String(png);

        EntityReviewService.TryGetPngSize(uri, out var w, out var h).Should().BeTrue();
        (w, h).Should().Be((1650, 2250));
        EntityReviewService.TryGetPngSize("data:image/jpeg;base64,AAAA", out _, out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetOverrides_lists_only_the_entitys_overrides()
    {
        await SeedDefault();
        await Svc().OverrideAsync(Goblin, "fields.cr", J("\"2\""), "r", CancellationToken.None);

        (await Svc().GetOverridesAsync(Goblin, CancellationToken.None)).Should().ContainSingle();
        (await Svc().GetOverridesAsync(Orc, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task PageImage_rejects_a_page_outside_the_entity_provenance()
    {
        await SeedDefault();

        var act = () => Svc().GetPageImageDataUriAsync(Orc, 99, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── R20: the legacy HTTP triage skips programmatic entities ────────────────

    private NeedsReviewService Legacy() => new(_loader, _writer, _ingestion, _tracker,
        Options.Create(new EntityExtractionOptions { CanonicalDirectory = _dir }));

    [Fact]
    public async Task Legacy_resolve_refuses_programmatic_entities()
    {
        await SeedDefault();

        var act = () => Legacy().ResolveAsync(Orc, "accept", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<ProgrammaticEntityReviewException>();
        (await FromFile(Orc)).Disposition.Should().Be(EntityDisposition.NeedsReview);
        await _ingestion.DidNotReceiveWithAnyArgs().ReindexEntityAsync(default, default!, default);
    }

    [Fact]
    public async Task Legacy_bulk_accept_skips_and_reports_programmatic_entities()
    {
        await SeedDefault();

        var result = await Legacy().BulkAcceptAsync(null, null, CancellationToken.None);

        result.Cleared.Should().Be(0);
        result.Skipped.Should().Be(1);
        (await FromFile(Orc)).Disposition.Should().Be(EntityDisposition.NeedsReview);
        await _ingestion.DidNotReceiveWithAnyArgs().ReindexEntityAsync(default, default!, default);
    }

    [Fact]
    public async Task Legacy_list_excludes_programmatic_entities()
    {
        await SeedDefault();

        (await Legacy().ListAsync(null, null, 0, 50, CancellationToken.None)).Total.Should().Be(0);
    }
}
