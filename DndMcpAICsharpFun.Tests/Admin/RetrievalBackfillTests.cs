using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Tests.VectorStore.Entities;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Qdrant.Client;
using Qdrant.Client.Grpc;

using Xunit;

using DomainSparseVector = DndMcpAICsharpFun.Infrastructure.Search.SparseVector;

namespace DndMcpAICsharpFun.Tests.Admin;

/// <summary>
/// Integration test against a real Qdrant instance (Testcontainers) covering the
/// one-time source_key backfill service (Task 4 of the stable source_key migration).
/// Docker must be running for this test to execute.
/// </summary>
[Collection("qdrant")]
public sealed class RetrievalBackfillTests : IAsyncLifetime
{
    private const string CollectionName = "dnd_blocks_retrieval_backfill_test";

    private readonly QdrantFixture _fixture;
    private QdrantClient _client = null!;
    private QdrantVectorStoreService _store = null!;
    private RetrievalBackfillService _service = null!;

    public RetrievalBackfillTests(QdrantFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var grpc = new Uri(_fixture.Container.GetGrpcConnectionString());
        _client = new QdrantClient(grpc.Host, grpc.Port);

        if (!await _client.CollectionExistsAsync(CollectionName))
        {
            await _client.CreateCollectionAsync(
                CollectionName,
                new VectorParams { Size = 4, Distance = Distance.Cosine });
        }

        var options = Options.Create(new QdrantOptions { BlocksCollectionName = CollectionName });
        var sparseState = new QdrantSparseState { SparseSupported = false };
        _store = new QdrantVectorStoreService(_client, options, sparseState, NullLogger<QdrantVectorStoreService>.Instance);
        _service = new RetrievalBackfillService(_store);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task BackfillAsync_stamps_source_key_onto_existing_blocks_and_is_idempotent()
    {
        var phbBlocks = Enumerable.Range(0, 3)
            .Select(i => MakeBlock("PlayerHandbook 2014", "hash-phb", i))
            .ToList();
        var mmBlocks = Enumerable.Range(0, 2)
            .Select(i => MakeBlock("Monster Manual 2014", "hash-mm", i))
            .ToList();

        // Seed blocks WITHOUT source_key (as legacy pre-migration data would look).
        await _store.UpsertBlocksAsync([.. phbBlocks, .. mmBlocks]);

        var result = await _service.BackfillAsync(CancellationToken.None);

        result["PHB"].Should().Be(3);
        result["MM"].Should().Be(2);

        var keyCounts = await _store.GetSourceKeyCountsAsync();
        keyCounts["PHB"].Should().Be(3);
        keyCounts["MM"].Should().Be(2);

        // Idempotent: running again re-stamps the same blocks and yields the same counts.
        var second = await _service.BackfillAsync(CancellationToken.None);
        second["PHB"].Should().Be(3);
        second["MM"].Should().Be(2);
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
}
