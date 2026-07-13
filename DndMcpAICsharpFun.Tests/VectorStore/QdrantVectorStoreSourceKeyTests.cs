using DndMcpAICsharpFun.Domain;
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

namespace DndMcpAICsharpFun.Tests.VectorStore;

/// <summary>
/// Integration test against a real Qdrant instance (Testcontainers) covering the
/// source_key/source_book counting and backfill methods added for the stable
/// source_key migration. Docker must be running for this test to execute.
/// </summary>
[Collection("qdrant")]
public sealed class QdrantVectorStoreSourceKeyTests : IAsyncLifetime
{
    private const string CollectionName = "dnd_blocks_source_key_test";

    private readonly QdrantFixture _fixture;
    private QdrantClient _client = null!;
    private QdrantVectorStoreService _store = null!;

    public QdrantVectorStoreSourceKeyTests(QdrantFixture fixture) => _fixture = fixture;

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
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Counts_by_source_book_then_backfill_counts_by_source_key()
    {
        var phbBlocks = Enumerable.Range(0, 3)
            .Select(i => MakeBlock("PlayerHandbook 2014", "hash-phb", i))
            .ToList();
        var mmBlocks = Enumerable.Range(0, 2)
            .Select(i => MakeBlock("Monster Manual 2014", "hash-mm", i))
            .ToList();

        await _store.UpsertBlocksAsync([.. phbBlocks, .. mmBlocks]);

        var bookCounts = await _store.GetSourceBookCountsAsync();
        bookCounts["PlayerHandbook 2014"].Should().Be(3);
        bookCounts["Monster Manual 2014"].Should().Be(2);

        var updatedCount = await _store.SetSourceKeyForBookAsync("PlayerHandbook 2014", "PHB");
        updatedCount.Should().Be(3);

        var keyCounts = await _store.GetSourceKeyCountsAsync();
        keyCounts["PHB"].Should().Be(3);
        keyCounts["MM"].Should().Be(0);
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
