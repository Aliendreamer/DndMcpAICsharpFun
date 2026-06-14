using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public sealed class ReindexEntityTests : IDisposable
{
    // ── Fake tracker ──────────────────────────────────────────────────────────

    private static IIngestionTracker MakeTracker(IngestionRecord record)
    {
        var t = Substitute.For<IIngestionTracker>();
        t.GetByIdAsync(record.Id, Arg.Any<CancellationToken>()).Returns(record);
        t.GetAllAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([record]);
        return t;
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private readonly string _dir;

    public ReindexEntityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ReindexEntityTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        // Copy the needs-review fixture (contains the "needs-review-book.*" entities).
        var src  = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", "needs-review-book.json");
        var dest = Path.Combine(_dir, "needs-review-book.json");
        File.Copy(src, dest);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private EntityIngestionOrchestrator BuildOrchestrator(RecordingEntityVectorStore store, IngestionRecord record)
    {
        var tracker     = MakeTracker(record);
        var loader      = new CanonicalJsonLoader();
        var dispatcher  = new EntityCanonicalTextDispatcher();
        var embeddings  = Substitute.For<IEmbeddingService>();
        embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var texts = ci.Arg<IList<string>>();
                return Task.FromResult<IList<float[]>>(
                    texts.Select(_ => new float[] { 0.1f, 0.2f, 0.3f }).ToArray());
            });

        var refResolver = new DndMcpAICsharpFun.Features.Entities.EntityReferenceResolver();
        var opts        = Options.Create(new EntityIngestionOptions
        {
            CanonicalDirectory = _dir,
            FivetoolsDirectory = Path.Combine(_dir, "5etools-absent"),   // absent → no enrichment
        });

        return new EntityIngestionOrchestrator(
            tracker, loader, dispatcher, refResolver, embeddings, store, opts,
            NullLogger<EntityIngestionOrchestrator>.Instance);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReindexEntityAsync_UpsertsExactlyOnePoint()
    {
        var store  = new RecordingEntityVectorStore();
        var record = new IngestionRecord
        {
            Id = 42, DisplayName = "needs-review-book", FileHash = "cafebabe",
            FilePath = "/tmp/fake.pdf", FileName = "fake.pdf",
            Version = "Edition2014", Status = IngestionStatus.EntitiesIngested,
        };

        var sut = BuildOrchestrator(store, record);

        await sut.ReindexEntityAsync(42, "needs-review-book.spell.fireball",
            CancellationToken.None);

        store.UpsertCalls.Should().HaveCount(1, "exactly one UpsertAsync call");
        store.UpsertCalls[0].Should().HaveCount(1, "exactly one point per call");
        store.UpsertCalls[0][0].Envelope.Id.Should().Be("needs-review-book.spell.fireball");
    }

    [Fact]
    public async Task ReindexEntityAsync_DoesNotCallDeleteByFileHashExcept()
    {
        var store  = new RecordingEntityVectorStore();
        var record = new IngestionRecord
        {
            Id = 42, DisplayName = "needs-review-book", FileHash = "cafebabe",
            FilePath = "/tmp/fake.pdf", FileName = "fake.pdf",
            Version = "Edition2014", Status = IngestionStatus.EntitiesIngested,
        };

        var sut = BuildOrchestrator(store, record);

        await sut.ReindexEntityAsync(42, "needs-review-book.spell.fireball",
            CancellationToken.None);

        store.DeleteByFileHashExceptCallCount.Should()
            .Be(0, "targeted reindex must never call DeleteByFileHashExceptAsync");
    }

    [Fact]
    public async Task ReindexEntityAsync_PointCarriesCorrectFileHash()
    {
        var store  = new RecordingEntityVectorStore();
        var record = new IngestionRecord
        {
            Id = 42, DisplayName = "needs-review-book", FileHash = "deadbeef",
            FilePath = "/tmp/fake.pdf", FileName = "fake.pdf",
            Version = "Edition2014", Status = IngestionStatus.EntitiesIngested,
        };

        var sut = BuildOrchestrator(store, record);
        await sut.ReindexEntityAsync(42, "needs-review-book.class.fighter", CancellationToken.None);

        store.UpsertCalls[0][0].FileHash.Should().Be("deadbeef");
    }

    [Fact]
    public async Task ReindexEntityAsync_UnknownEntity_Throws()
    {
        var store  = new RecordingEntityVectorStore();
        var record = new IngestionRecord
        {
            Id = 42, DisplayName = "needs-review-book", FileHash = "cafebabe",
            FilePath = "/tmp/fake.pdf", FileName = "fake.pdf",
            Version = "Edition2014", Status = IngestionStatus.EntitiesIngested,
        };

        var sut = BuildOrchestrator(store, record);

        var act = async () => await sut.ReindexEntityAsync(
            42, "nonexistent.entity.id", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
