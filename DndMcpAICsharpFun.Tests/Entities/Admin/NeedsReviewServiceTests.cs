using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;

using FluentAssertions;

using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public sealed class NeedsReviewServiceTests : IDisposable
{
    // ── Setup / teardown ──────────────────────────────────────────────────────

    private readonly string _dir;
    private readonly CanonicalJsonLoader _loader = new();
    private readonly CanonicalJsonWriter _writer = new();
    private readonly IEntityIngestionOrchestrator _orchestrator;
    private readonly IIngestionTracker _tracker;

    public NeedsReviewServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"NeedsReviewServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);

        // Copy the fixture that has 2 NeedsReview=true entities.
        var src = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", "needs-review-book.json");
        File.Copy(src, Path.Combine(_dir, "needs-review-book.json"));

        // Orchestrator and tracker are faked — we only care about the canonical file side.
        _orchestrator = Substitute.For<IEntityIngestionOrchestrator>();
        _orchestrator.ReindexEntityAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var record = new IngestionRecord
        {
            Id = 1,
            DisplayName = "needs-review-book",
            FilePath = "/tmp/fake.pdf",
            FileName = "fake.pdf",
            FileHash = "cafebabe",
            Version = "Edition2014",
            Status = IngestionStatus.EntitiesIngested,
        };
        _tracker = Substitute.For<IIngestionTracker>();
        _tracker.GetAllAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<IngestionRecord> { record });
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private NeedsReviewService BuildSut() => new(
        _loader,
        _writer,
        _orchestrator,
        _tracker,
        Options.Create(new EntityExtractionOptions { CanonicalDirectory = _dir }));

    // ── List tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsOnlyNeedsReviewEntities()
    {
        var sut = BuildSut();
        var result = await sut.ListAsync(null, null, 0, 50, CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(2);
        result.Items.Select(i => i.Id).Should()
            .Contain("needs-review-book.spell.fireball")
            .And.Contain("needs-review-book.monster.b ullywug");
    }

    [Fact]
    public async Task List_FilterByBook_OnlyMatchingBookReturned()
    {
        var sut = BuildSut();
        var result = await sut.ListAsync("needs-review-book", null, 0, 50, CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(i => i.Book == "needs-review-book");
    }

    [Fact]
    public async Task List_FilterByNonExistingBook_EmptyResult()
    {
        var sut = BuildSut();
        var result = await sut.ListAsync("nonexistent-book", null, 0, 50, CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task List_DeriveReason_LowConfidenceForCleanName()
    {
        var sut = BuildSut();
        var result = await sut.ListAsync(null, null, 0, 50, CancellationToken.None);

        // "Fireball" has no OCR artifacts → low-confidence.
        var fireball = result.Items.Single(i => i.Id == "needs-review-book.spell.fireball");
        fireball.Reason.Should().Be("low-confidence");
    }

    [Fact]
    public async Task List_DeriveReason_OcrArtifactForSplitName()
    {
        var sut = BuildSut();
        var result = await sut.ListAsync(null, null, 0, 50, CancellationToken.None);

        // "B ullywug" has a split-word OCR artifact → ocr-artifact.
        var bullywug = result.Items.Single(i => i.Id == "needs-review-book.monster.b ullywug");
        bullywug.Reason.Should().Be("ocr-artifact");
    }

    [Fact]
    public async Task List_FilterByReason_OnlyOcrArtifact()
    {
        var sut = BuildSut();
        var result = await sut.ListAsync(null, "ocr-artifact", 0, 50, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be("needs-review-book.monster.b ullywug");
    }

    [Fact]
    public async Task List_Paging_OffsetAndLimit()
    {
        var sut = BuildSut();
        var result = await sut.ListAsync(null, null, 1, 1, CancellationToken.None);

        result.Total.Should().Be(2, "total includes all matches, not just the page");
        result.Items.Should().HaveCount(1, "limit=1");
    }

    // ── Get tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_KnownId_ReturnsEntity()
    {
        var sut = BuildSut();
        var entity = await sut.GetAsync("needs-review-book.spell.fireball", CancellationToken.None);

        entity.Should().NotBeNull();
        entity!.Id.Should().Be("needs-review-book.spell.fireball");
        entity.Name.Should().Be("Fireball");
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNull()
    {
        var sut = BuildSut();
        var entity = await sut.GetAsync("nonexistent.entity.id", CancellationToken.None);

        entity.Should().BeNull();
    }

    // ── Resolve: accept ───────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_Accept_ClearsFlagAndCallsReindex()
    {
        var sut = BuildSut();
        var found = await sut.ResolveAsync(
            "needs-review-book.spell.fireball", "accept", null, null, CancellationToken.None);

        found.Should().BeTrue();

        // Flag cleared in canonical file.
        var after = await _loader.LoadAsync(
            Path.Combine(_dir, "needs-review-book.json"), CancellationToken.None);
        after.Entities.Single(e => e.Id == "needs-review-book.spell.fireball")
            .NeedsReview.Should().BeFalse();

        // Reindex was called exactly once for this entity.
        await _orchestrator.Received(1).ReindexEntityAsync(
            Arg.Any<int>(), "needs-review-book.spell.fireball", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_Accept_DoesNotChangeNameOrFields()
    {
        var sut = BuildSut();
        await sut.ResolveAsync(
            "needs-review-book.spell.fireball", "accept", "IGNORED_NAME", null, CancellationToken.None);

        var after = await _loader.LoadAsync(
            Path.Combine(_dir, "needs-review-book.json"), CancellationToken.None);
        after.Entities.Single(e => e.Id == "needs-review-book.spell.fireball")
            .Name.Should().Be("Fireball", "accept must not rename");
    }

    // ── Resolve: edit ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_Edit_AppliesNameAndClearsFlag()
    {
        var sut = BuildSut();
        var found = await sut.ResolveAsync(
            "needs-review-book.spell.fireball", "edit", "Fireball (Fixed)", null, CancellationToken.None);

        found.Should().BeTrue();

        var after = await _loader.LoadAsync(
            Path.Combine(_dir, "needs-review-book.json"), CancellationToken.None);
        var entity = after.Entities.Single(e => e.Id == "needs-review-book.spell.fireball");
        entity.Name.Should().Be("Fireball (Fixed)");
        entity.NeedsReview.Should().BeFalse();
    }

    // ── Resolve: unknown id ───────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_UnknownId_ReturnsFalse()
    {
        var sut = BuildSut();
        var found = await sut.ResolveAsync(
            "nonexistent.entity.id", "accept", null, null, CancellationToken.None);

        found.Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_BadAction_Throws()
    {
        var sut = BuildSut();
        var act = async () => await sut.ResolveAsync(
            "needs-review-book.spell.fireball", "destroy", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*action*");
    }

    // ── Resolve: already-resolved (idempotent) ────────────────────────────────

    [Fact]
    public async Task Resolve_AlreadyResolved_IdempotentSuccess()
    {
        var sut = BuildSut();

        // First resolve.
        await sut.ResolveAsync(
            "needs-review-book.spell.fireball", "accept", null, null, CancellationToken.None);

        // Second resolve on the same entity (flag already false) — must not throw.
        var secondResult = await sut.ResolveAsync(
            "needs-review-book.spell.fireball", "accept", null, null, CancellationToken.None);

        secondResult.Should().BeTrue();
    }

    // ── Bulk accept ───────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkAccept_NoFilter_ClearsAllFlaggedEntities()
    {
        var sut = BuildSut();
        var cleared = await sut.BulkAcceptAsync(null, null, CancellationToken.None);

        cleared.Should().Be(2);

        var after = await _loader.LoadAsync(
            Path.Combine(_dir, "needs-review-book.json"), CancellationToken.None);
        after.Entities.Should().OnlyContain(e => !e.NeedsReview);
    }

    [Fact]
    public async Task BulkAccept_FilterByReason_OnlyClearsMatchingEntities()
    {
        var sut = BuildSut();
        var cleared = await sut.BulkAcceptAsync(null, "low-confidence", CancellationToken.None);

        cleared.Should().Be(1, "only the low-confidence entity matches");

        var after = await _loader.LoadAsync(
            Path.Combine(_dir, "needs-review-book.json"), CancellationToken.None);

        // "Fireball" (low-confidence) should be cleared.
        after.Entities.Single(e => e.Id == "needs-review-book.spell.fireball")
            .NeedsReview.Should().BeFalse();

        // "B ullywug" (ocr-artifact) must remain flagged.
        after.Entities.Single(e => e.Id == "needs-review-book.monster.b ullywug")
            .NeedsReview.Should().BeTrue();
    }

    [Fact]
    public async Task BulkAccept_FilterByBook_OnlyClearsMatchingBook()
    {
        var sut = BuildSut();
        var cleared = await sut.BulkAcceptAsync("needs-review-book", null, CancellationToken.None);

        cleared.Should().Be(2);
    }

    [Fact]
    public async Task BulkAccept_FilterByNonExistingBook_ReturnsZero()
    {
        var sut = BuildSut();
        var cleared = await sut.BulkAcceptAsync("no-such-book", null, CancellationToken.None);

        cleared.Should().Be(0);
    }

    [Fact]
    public async Task BulkAccept_CallsReindexForEachClearedEntity()
    {
        var sut = BuildSut();
        await sut.BulkAcceptAsync(null, null, CancellationToken.None);

        // Both entities should have been reindexed.
        await _orchestrator.Received(1).ReindexEntityAsync(
            Arg.Any<int>(), "needs-review-book.spell.fireball", Arg.Any<CancellationToken>());
        await _orchestrator.Received(1).ReindexEntityAsync(
            Arg.Any<int>(), "needs-review-book.monster.b ullywug", Arg.Any<CancellationToken>());
    }
}