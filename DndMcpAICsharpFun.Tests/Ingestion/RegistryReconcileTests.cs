using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Tests.Persistence;
using DndMcpAICsharpFun.Tests.VectorStore.Entities;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Qdrant.Client;
using Qdrant.Client.Grpc;

using Xunit;

using DomainSparseVector = DndMcpAICsharpFun.Infrastructure.Search.SparseVector;

namespace DndMcpAICsharpFun.Tests.Ingestion;

/// <summary>
/// Integration test proving the metadata-only registry reconcile: a past DB reset can leave
/// <c>IngestionRecords</c> missing rows for books whose blocks are still present in Qdrant
/// (<c>dnd_blocks</c>). <see cref="RegistryReconcileService"/> must create the missing record
/// from the block count alone — no re-ingest, no re-embed — and leave any already-tracked book
/// untouched. Real Postgres (<see cref="PostgresFixture"/>) and real Qdrant
/// (<see cref="QdrantFixture"/>) via Testcontainers; Docker must be running.
/// </summary>
public sealed class RegistryReconcileTests : IClassFixture<QdrantFixture>, IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly QdrantFixture _qdrantFixture;
    private readonly PostgresFixture _pgFixture;
    private readonly string _collectionName = $"dnd_blocks_registry_reconcile_test_{Guid.NewGuid():N}";

    private QdrantClient _client = null!;
    private QdrantVectorStoreService _store = null!;
    private IIngestionTracker _tracker = null!;
    private RegistryReconcileService _service = null!;

    public RegistryReconcileTests(QdrantFixture qdrantFixture, PostgresFixture pgFixture)
    {
        _qdrantFixture = qdrantFixture;
        _pgFixture = pgFixture;
    }

    public async Task InitializeAsync()
    {
        await _pgFixture.ResetAsync();

        var grpc = new Uri(_qdrantFixture.Container.GetGrpcConnectionString());
        _client = new QdrantClient(grpc.Host, grpc.Port);

        if (!await _client.CollectionExistsAsync(_collectionName))
        {
            await _client.CreateCollectionAsync(
                _collectionName,
                new VectorParams { Size = 4, Distance = Distance.Cosine });
        }

        var options = Options.Create(new QdrantOptions { BlocksCollectionName = _collectionName });
        var sparseState = new QdrantSparseState { SparseSupported = false };
        _store = new QdrantVectorStoreService(_client, options, sparseState, NullLogger<QdrantVectorStoreService>.Instance);

        _tracker = new IngestionTracker(new TestDb(_pgFixture));
        _service = new RegistryReconcileService(_tracker, _store);
    }

    public async Task DisposeAsync()
    {
        try { await _client.DeleteCollectionAsync(_collectionName); } catch { /* best-effort cleanup */ }
        _client.Dispose();
    }

    private static (BlockChunk Chunk, float[] Vector, DomainSparseVector Sparse, string FileHash) MakeBlock(
        string sourceBook, string fileHash, int index)
    {
        var metadata = new BlockMetadata(
            SourceBook: sourceBook,
            Version: DndVersion.Edition2014,
            Category: ContentCategory.Rule,
            SectionTitle: $"Section {index}",
            SectionStart: index,
            SectionEnd: index + 1,
            PageNumber: index + 1,
            BlockOrder: index,
            GlobalIndex: index);
        var chunk = new BlockChunk($"block text {index}", metadata);
        var vector = Enumerable.Range(0, 4).Select(i => (float)(index + i) / 10f).ToArray();
        var sparse = new DomainSparseVector([], []);
        return (chunk, vector, sparse, fileHash);
    }

    private Task<IngestionRecord> SeedPhbRecordAsync() => _tracker.CreateAsync(new IngestionRecord
    {
        FilePath = "/tmp/phb.pdf",
        FileName = "phb.pdf",
        FileHash = "phb-hash",
        Version = DndVersion.Edition2014.ToString(),
        DisplayName = "PlayerHandbook 2014",
        Status = IngestionStatus.EntitiesIngested,
        ChunkCount = 999,
        EntityCount = 42,
        FivetoolsSourceKey = "PHB",
    });

    [Fact]
    public async Task ReconcileAsync_creates_missing_record_from_existing_blocks_and_is_a_noop_on_second_run()
    {
        var mmBlocks = Enumerable.Range(0, 5)
            .Select(i => MakeBlock("Monster Manual 2014", "mm-hash", i))
            .ToList();
        await _store.UpsertBlocksAsync(mmBlocks);
        await SeedPhbRecordAsync();

        var created = await _service.ReconcileAsync(CancellationToken.None);

        created.Should().BeEquivalentTo(["Monster Manual 2014"]);

        var all = await _tracker.GetAllAsync(limit: 100, offset: 0, ct: CancellationToken.None);

        var mmRecord = all.Should().ContainSingle(r => r.DisplayName == "Monster Manual 2014").Subject;
        mmRecord.ChunkCount.Should().Be(5);
        mmRecord.Status.Should().Be(IngestionStatus.EntitiesIngested);
        mmRecord.FivetoolsSourceKey.Should().Be("MM");
        mmRecord.EntityCount.Should().BeNull();

        var phbRecord = all.Should().ContainSingle(r => r.DisplayName == "PlayerHandbook 2014").Subject;
        phbRecord.ChunkCount.Should().Be(999, "reconcile must never touch an existing record");
        phbRecord.EntityCount.Should().Be(42);

        var second = await _service.ReconcileAsync(CancellationToken.None);
        second.Should().BeEmpty("a second run must be a no-op once every book with blocks is tracked");
    }
}
