using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Tests.TestDoubles;
using DndMcpAICsharpFun.Tests.VectorStore.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Xunit;
using DomainSparseVector = DndMcpAICsharpFun.Infrastructure.Search.SparseVector;

namespace DndMcpAICsharpFun.Tests.Admin;

/// <summary>
/// Integration test proving the entity-grounding-cascade I-1 invariant end-to-end against a REAL
/// Qdrant <c>dnd_entities</c> collection (Testcontainers): a promoted entity is (re)indexed, and an
/// entity marked Ungrounded is genuinely DELETED — never left behind as a stale fabrication.
/// Unlike <see cref="RegroundServiceTests"/> (which mocks the orchestrator/vector-store to unit-test
/// <see cref="RegroundService"/>'s own logic), this test wires the REAL
/// <see cref="EntityIngestionOrchestrator"/> and REAL <see cref="QdrantEntityVectorStore"/> so the
/// write path (reindex vs. delete) is exercised against genuine infra. Only the grading cascade
/// (already unit-tested) and the Postgres-backed ingestion tracker are faked, so no Ollama/Postgres
/// is required — Docker (for the Qdrant Testcontainer) is the only prerequisite.
/// </summary>
[Collection("qdrant")]
public sealed class RegroundServiceIntegrationTests : IAsyncLifetime
{
    private const string EntitiesCollectionName = "dnd_entities_reground_test";
    private const string BlocksCollectionName = "dnd_blocks_reground_test";
    private const string BookSlug = "reground-int-book";
    private const int BookId = 1;
    private const int VectorSize = 4;

    private readonly QdrantFixture _fixture;
    private readonly CanonicalJsonLoader _loader = new();
    private readonly CanonicalJsonWriter _writer = new();

    private QdrantClient _client = null!;
    private QdrantEntityVectorStore _vectorStore = null!;
    private string _dir = null!;

    public RegroundServiceIntegrationTests(QdrantFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var grpc = new Uri(_fixture.Container.GetGrpcConnectionString());
        _client = new QdrantClient(grpc.Host, grpc.Port);

        if (!await _client.CollectionExistsAsync(EntitiesCollectionName))
        {
            await _client.CreateCollectionAsync(
                EntitiesCollectionName,
                new VectorParams { Size = VectorSize, Distance = Distance.Cosine });
        }

        var qdrantOptions = Options.Create(new QdrantOptions
        {
            EntitiesCollectionName = EntitiesCollectionName,
            BlocksCollectionName = BlocksCollectionName,
        });
        _vectorStore = new QdrantEntityVectorStore(_client, qdrantOptions);

        _dir = Path.Combine(Path.GetTempPath(), $"RegroundServiceIntegrationTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        return Task.CompletedTask;
    }

    private string CanonicalPath => Path.Combine(_dir, BookSlug + ".json");

    // ── Fakes (cascade + tracker only — everything touching Qdrant is real) ────

    private sealed class FakeGroundingCascade(Dictionary<string, GroundingVerdict> verdicts) : IGroundingCascade
    {
        public Task<GroundingVerdict> GradeAsync(
            EntityEnvelope entity, string sourceProse, bool judgeEnabled, CancellationToken ct) =>
            Task.FromResult(verdicts[entity.Id]);
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

    private async Task SeedQdrantPointsAsync(params EntityEnvelope[] entities)
    {
        var points = entities
            .Select(e => new EntityPoint(e, FixedVector(), "cafebabe"))
            .ToList();
        await _vectorStore.UpsertAsync(points);
    }

    private static float[] FixedVector() => [0.1f, 0.2f, 0.3f, 0.4f];

    private RegroundService BuildSut(IGroundingCascade cascade, out IEntityIngestionOrchestrator orchestrator)
    {
        var record = new IngestionRecord
        {
            Id = BookId, DisplayName = BookSlug,
            FilePath = "/tmp/fake.pdf", FileName = "fake.pdf",
            FileHash = "cafebabe", Version = "Edition2014",
            Status = IngestionStatus.EntitiesIngested,
        };
        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetByIdAsync(BookId, Arg.Any<CancellationToken>()).Returns(record);

        var embeddings = new StubEmbeddingService(dimensions: VectorSize);

        // Real orchestrator over the real Qdrant-backed vector store: the whole point of this
        // integration test is that ReindexEntityAsync's upsert and RegroundService's
        // DeleteByIdsAsync both hit genuine infra.
        orchestrator = new EntityIngestionOrchestrator(
            tracker,
            _loader,
            new EntityCanonicalTextDispatcher(),
            new EntityReferenceResolver(),
            embeddings,
            _vectorStore,
            Options.Create(new EntityIngestionOptions
            {
                CanonicalDirectory = _dir,
                FivetoolsDirectory = $"5etools-absent-{Guid.NewGuid():N}",
            }),
            NullLogger<EntityIngestionOrchestrator>.Instance);

        return new RegroundService(
            _loader,
            _writer,
            orchestrator,
            tracker,
            cascade,
            embeddings,
            new FakeQdrantSearchClient(),
            _vectorStore,
            Options.Create(new QdrantOptions
            {
                EntitiesCollectionName = EntitiesCollectionName,
                BlocksCollectionName = BlocksCollectionName,
            }),
            Options.Create(new GroundingOptions()),
            Options.Create(new EntityExtractionOptions { CanonicalDirectory = _dir }));
    }

    // ── Test ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegroundAsync_PromotedEntityIsIndexed_UngroundedEntityIsDeleted_FromRealQdrant()
    {
        var grounded = MakeEntity($"{BookSlug}.spell.grounded-alpha", "Grounded Alpha");
        var ungrounded = MakeEntity($"{BookSlug}.spell.ungrounded-bravo", "Ungrounded Bravo");
        var uncertain = MakeEntity($"{BookSlug}.spell.uncertain-charlie", "Uncertain Charlie");

        await SeedCanonicalAsync(grounded, ungrounded, uncertain);
        // Pre-seed all 3 as already present in dnd_entities, as if previously ingested while
        // NeedsReview — this is what makes the entity-B-absent assertion meaningful: it starts
        // present and must be actively removed by the reground pass.
        await SeedQdrantPointsAsync(grounded, ungrounded, uncertain);

        var cascade = new FakeGroundingCascade(new Dictionary<string, GroundingVerdict>
        {
            [grounded.Id] = new GroundingVerdict(GroundingStatus.Grounded, DecidedByTier: 0, Score: 1.0),
            [ungrounded.Id] = new GroundingVerdict(GroundingStatus.Ungrounded, DecidedByTier: 2, Score: 0.2),
            [uncertain.Id] = new GroundingVerdict(GroundingStatus.Uncertain, DecidedByTier: 1, Score: 0.6),
        });

        var sut = BuildSut(cascade, out _);

        var result = await sut.RegroundAsync(BookId, judge: true, CancellationToken.None);

        result.Should().Be(new RegroundResult(
            Scanned: 3, Promoted: 1, MarkedUngrounded: 1, StillFlagged: 1, Tier2Invoked: 1));

        // ── Real-Qdrant write-path assertions (the point of this test) ─────────
        (await _vectorStore.GetByIdAsync(grounded.Id)).Should().NotBeNull(
            "the promoted entity must be (re)indexed into dnd_entities");
        (await _vectorStore.GetByIdAsync(ungrounded.Id)).Should().BeNull(
            "a judge-confirmed fabrication must be DELETED from dnd_entities, not merely left stale");
        (await _vectorStore.GetByIdAsync(uncertain.Id)).Should().NotBeNull(
            "an entity that remains Uncertain/NeedsReview must be left untouched in dnd_entities");

        // ── Canonical JSON write-back assertions ────────────────────────────────
        var after = await _loader.LoadAsync(CanonicalPath, CancellationToken.None);
        after.Entities.Single(e => e.Id == grounded.Id).Disposition.Should().Be(EntityDisposition.Accepted);
        after.Entities.Single(e => e.Id == grounded.Id).NeedsReview.Should().BeFalse();
        after.Entities.Single(e => e.Id == ungrounded.Id).Disposition.Should().Be(EntityDisposition.Ungrounded);
        after.Entities.Single(e => e.Id == uncertain.Id).Disposition.Should().Be(EntityDisposition.NeedsReview);
        after.Entities.Single(e => e.Id == uncertain.Id).NeedsReview.Should().BeTrue();
    }
}
