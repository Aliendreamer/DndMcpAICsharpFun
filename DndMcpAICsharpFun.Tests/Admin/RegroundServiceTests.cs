using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Qdrant.Client.Grpc;
using Xunit;
using DomainSparseVector = DndMcpAICsharpFun.Infrastructure.Search.SparseVector;

namespace DndMcpAICsharpFun.Tests.Admin;

public sealed class RegroundServiceTests : IDisposable
{
    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeGroundingCascade : IGroundingCascade
    {
        private readonly Dictionary<string, GroundingVerdict> _verdicts;
        public List<string> CalledIds { get; } = [];

        public FakeGroundingCascade(Dictionary<string, GroundingVerdict> verdicts) => _verdicts = verdicts;

        public Task<GroundingVerdict> GradeAsync(
            EntityEnvelope entity, string sourceProse, bool judgeEnabled, CancellationToken ct)
        {
            CalledIds.Add(entity.Id);
            return Task.FromResult(_verdicts[entity.Id]);
        }
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public Task<IList<float[]>> EmbedAsync(IList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IList<float[]>>(texts.Select(_ => new float[] { 0.1f, 0.2f }).ToList());
    }

    private sealed class FakeQdrantSearchClient : IQdrantSearchClient
    {
        public Task<IReadOnlyList<ScoredPoint>> SearchAsync(
            string collectionName,
            ReadOnlyMemory<float> vector,
            Filter? filter = null,
            ulong limit = 10,
            float? scoreThreshold = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScoredPoint>>([]);

        public Task<IReadOnlyList<ScoredPoint>> QueryAsync(
            string collectionName,
            ReadOnlyMemory<float> denseVector,
            DomainSparseVector sparseVector,
            Filter? filter = null,
            ulong limit = 10,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("RegroundService does not use hybrid query.");
    }

    // ── Setup / teardown ──────────────────────────────────────────────────────

    private readonly string _dir;
    private const string BookSlug = "needs-review-book";

    private readonly CanonicalJsonLoader _loader = new();
    private readonly CanonicalJsonWriter _writer = new();
    private readonly IEntityIngestionOrchestrator _orchestrator;
    private readonly IEntityVectorStore _vectorStore;
    private readonly IIngestionTracker _tracker;

    public RegroundServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"RegroundServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);

        _orchestrator = Substitute.For<IEntityIngestionOrchestrator>();
        _orchestrator.ReindexEntityAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _vectorStore = Substitute.For<IEntityVectorStore>();
        _vectorStore.DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var record = new IngestionRecord
        {
            Id = 1, DisplayName = BookSlug,
            FilePath = "/tmp/fake.pdf", FileName = "fake.pdf",
            FileHash = "cafebabe", Version = "Edition2014",
            Status = IngestionStatus.EntitiesIngested,
        };
        _tracker = Substitute.For<IIngestionTracker>();
        _tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CanonicalPath => Path.Combine(_dir, BookSlug + ".json");
    private string CheckpointPath => Path.Combine(_dir, BookSlug + ".reground.progress.json");

    private static EntityEnvelope MakeEntity(string id, string name) => new(
        Id: id,
        Type: EntityType.Spell,
        Name: name,
        SourceBook: BookSlug,
        Edition: "Edition2014",
        Page: 10,
        FirstAppearedIn: new FirstAppearance(BookSlug, "Edition2014", 10),
        RevisedIn: [],
        SettingTags: [],
        CanonicalText: $"{name} canonical text.",
        Fields: JsonDocument.Parse("{}").RootElement.Clone(),
        NeedsReview: true,
        Disposition: EntityDisposition.NeedsReview);

    private async Task SeedCanonicalAsync(params EntityEnvelope[] entities)
    {
        var file = new CanonicalJsonFile(
            SchemaVersion: CanonicalJsonSchema.CurrentVersion,
            Book: new CanonicalBookMetadata(BookSlug, "Edition2014", "cafebabe", BookSlug),
            Entities: entities);
        await _writer.WriteAsync(CanonicalPath, file, CancellationToken.None);
    }

    private RegroundService BuildSut(IGroundingCascade cascade) => new(
        _loader,
        _writer,
        _orchestrator,
        _tracker,
        cascade,
        new FakeEmbeddingService(),
        new FakeQdrantSearchClient(),
        _vectorStore,
        Options.Create(new QdrantOptions { BlocksCollectionName = "dnd_blocks" }),
        Options.Create(new GroundingOptions()),
        Options.Create(new EntityExtractionOptions { CanonicalDirectory = _dir }));

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegroundAsync_MixedVerdicts_AppliesPolicyAndReturnsCounts()
    {
        var e1 = MakeEntity($"{BookSlug}.spell.grounded-one", "Grounded One");
        var e2 = MakeEntity($"{BookSlug}.spell.ungrounded-two", "Ungrounded Two");
        var e3 = MakeEntity($"{BookSlug}.spell.uncertain-three", "Uncertain Three");
        await SeedCanonicalAsync(e1, e2, e3);

        var cascade = new FakeGroundingCascade(new Dictionary<string, GroundingVerdict>
        {
            [e1.Id] = new GroundingVerdict(GroundingStatus.Grounded,   DecidedByTier: 0, Score: 1.0),
            [e2.Id] = new GroundingVerdict(GroundingStatus.Ungrounded, DecidedByTier: 2, Score: 0.3),
            [e3.Id] = new GroundingVerdict(GroundingStatus.Uncertain,  DecidedByTier: 1, Score: 0.6),
        });

        var sut = BuildSut(cascade);
        var result = await sut.RegroundAsync(1, judge: true, CancellationToken.None);

        result.Should().Be(new RegroundResult(Scanned: 3, Promoted: 1, MarkedUngrounded: 1, StillFlagged: 1, Tier2Invoked: 1));

        var after = await _loader.LoadAsync(CanonicalPath, CancellationToken.None);
        after.Entities.Single(e => e.Id == e1.Id).Disposition.Should().Be(EntityDisposition.Accepted);
        after.Entities.Single(e => e.Id == e1.Id).NeedsReview.Should().BeFalse();
        after.Entities.Single(e => e.Id == e2.Id).Disposition.Should().Be(EntityDisposition.Ungrounded);
        after.Entities.Single(e => e.Id == e3.Id).Disposition.Should().Be(EntityDisposition.NeedsReview);
        after.Entities.Single(e => e.Id == e3.Id).NeedsReview.Should().BeTrue();

        // Promoted (e1) is reindexed. Ungrounded (e2) must be DELETED from the vector store —
        // never reindexed/upserted, which would refresh the fabrication instead of removing it.
        await _orchestrator.Received(1).ReindexEntityAsync(1, e1.Id, Arg.Any<CancellationToken>());
        await _orchestrator.DidNotReceive().ReindexEntityAsync(1, e2.Id, Arg.Any<CancellationToken>());
        await _orchestrator.DidNotReceive().ReindexEntityAsync(1, e3.Id, Arg.Any<CancellationToken>());

        await _vectorStore.Received(1).DeleteByIdsAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.Count == 1 && ids.Contains(e2.Id)),
            Arg.Any<CancellationToken>());

        File.Exists(CanonicalPath).Should().BeTrue("canonical file must never be deleted");
        File.Exists(CheckpointPath).Should().BeFalse("checkpoint sidecar is deleted on success");
    }

    [Fact]
    public async Task RegroundAsync_PreSeededCheckpoint_SkipsAlreadyProcessedIds()
    {
        var e1 = MakeEntity($"{BookSlug}.spell.grounded-one", "Grounded One");
        var e2 = MakeEntity($"{BookSlug}.spell.ungrounded-two", "Ungrounded Two");
        await SeedCanonicalAsync(e1, e2);

        // Pre-seed a checkpoint claiming e1 is already processed from a prior (crashed) run.
        await File.WriteAllTextAsync(CheckpointPath, JsonSerializer.Serialize(new[] { e1.Id }));

        var cascade = new FakeGroundingCascade(new Dictionary<string, GroundingVerdict>
        {
            [e1.Id] = new GroundingVerdict(GroundingStatus.Grounded, DecidedByTier: 0, Score: 1.0),
            [e2.Id] = new GroundingVerdict(GroundingStatus.Ungrounded, DecidedByTier: 2, Score: 0.2),
        });

        var sut = BuildSut(cascade);
        await sut.RegroundAsync(1, judge: true, CancellationToken.None);

        cascade.CalledIds.Should().NotContain(e1.Id, "an id already recorded in the checkpoint must be skipped");
        cascade.CalledIds.Should().Contain(e2.Id);
    }

    [Fact]
    public async Task RegroundAsync_CrashAfterFlush_ReindexesCheckpointRecordedChangedIds()
    {
        // Prior (crashed) run: `alreadyAccepted` was graded and promoted — canonical already
        // reflects Disposition.Accepted — and its id was recorded in the checkpoint's changedIds,
        // but the process crashed before the final reindex loop ran for it. On resume,
        // `alreadyAccepted` is no longer flagged (IsFlagged is false), so it will never again be
        // selected by the flaggedIds loop; only a checkpoint-seeded changedIds set can recover it.
        var alreadyAccepted = MakeEntity($"{BookSlug}.spell.already-accepted", "Already Accepted") with
        {
            Disposition = EntityDisposition.Accepted, NeedsReview = false,
        };
        var stillFlagged = MakeEntity($"{BookSlug}.spell.still-flagged", "Still Flagged");
        await SeedCanonicalAsync(alreadyAccepted, stillFlagged);

        var checkpointJson = JsonSerializer.Serialize(new
        {
            doneIds = new[] { alreadyAccepted.Id },
            changedIds = new[] { alreadyAccepted.Id },
        });
        await File.WriteAllTextAsync(CheckpointPath, checkpointJson);

        var cascade = new FakeGroundingCascade(new Dictionary<string, GroundingVerdict>
        {
            [stillFlagged.Id] = new GroundingVerdict(GroundingStatus.Grounded, DecidedByTier: 0, Score: 1.0),
        });

        var sut = BuildSut(cascade);
        await sut.RegroundAsync(1, judge: true, CancellationToken.None);

        await _orchestrator.Received(1).ReindexEntityAsync(1, alreadyAccepted.Id, Arg.Any<CancellationToken>());
    }
}
