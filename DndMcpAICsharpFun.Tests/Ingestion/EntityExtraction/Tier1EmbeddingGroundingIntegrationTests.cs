using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Tests.TestDoubles;
using DndMcpAICsharpFun.Tests.VectorStore.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

/// <summary>
/// Integration test against a REAL Qdrant instance (Testcontainers) proving that
/// <see cref="Tier1EmbeddingGrounding"/>'s book+page scoping actually filters search results —
/// not merely that the Qdrant <c>Filter</c> object is constructed correctly (that is already
/// covered by the fake-based <see cref="Tier1EmbeddingGroundingTests"/>). Docker must be running
/// for this test to execute.
/// </summary>
[Collection("qdrant")]
public sealed class Tier1EmbeddingGroundingIntegrationTests : IAsyncLifetime
{
    private const int VectorSize = 4;
    private const double SimilarityFloor = 0.5;
    private const int PageWindow = 2;
    private const string EntityText = "a fireball spell that deals fire damage in a 20-foot radius";

    // Fixed query embedding returned by the stub for any entity text: cosine similarity to this
    // vector is 1.0 for an identical seeded vector and 0.0 for an orthogonal one, so similarity is
    // fully controlled by which blocks are seeded (and whether the scoping filter finds them).
    private static readonly float[] QueryVector = [1f, 0f, 0f, 0f];

    private readonly QdrantFixture _fixture;
    private readonly string _collectionName = $"dnd_blocks_tier1_test_{Guid.NewGuid():N}";
    private QdrantClient _client = null!;
    private Tier1EmbeddingGrounding _sut = null!;

    public Tier1EmbeddingGroundingIntegrationTests(QdrantFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var grpc = new Uri(_fixture.Container.GetGrpcConnectionString());
        _client = new QdrantClient(grpc.Host, grpc.Port);

        await _client.CreateCollectionAsync(
            _collectionName,
            new VectorParams { Size = VectorSize, Distance = Distance.Cosine });

        // Mirrors QdrantCollectionInitializer.CreatePayloadIndexesAsync for the two fields
        // Tier1EmbeddingGrounding filters on, so the real filter behaves as it would in production.
        await _client.CreatePayloadIndexAsync(_collectionName, QdrantPayloadFields.SourceBook, PayloadSchemaType.Keyword);
        await _client.CreatePayloadIndexAsync(_collectionName, QdrantPayloadFields.PageNumber, PayloadSchemaType.Integer);

        var qdrantOptions = Options.Create(new QdrantOptions
        {
            BlocksCollectionName = _collectionName,
            VectorSize = VectorSize,
            Quantization = new QdrantQuantizationOptions { Enabled = false },
        });
        var groundingOptions = Options.Create(new GroundingOptions
        {
            SimilarityFloor = SimilarityFloor,
            PageWindow = PageWindow,
        });
        var embeddings = new StubEmbeddingService(VectorSize, _ => QueryVector);
        var searchClient = new QdrantSearchClientAdapter(_client, qdrantOptions);

        _sut = new Tier1EmbeddingGrounding(embeddings, searchClient, qdrantOptions, groundingOptions);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GroundAsync_matches_high_similarity_block_in_scope_for_book_and_page()
    {
        await SeedBlockAsync(QueryVector, sourceBook: "phb14", page: 100);

        var result = await _sut.GroundAsync(EntityText, "phb14", 100, CancellationToken.None);

        result.BelowFloor.Should().BeFalse();
    }

    [Fact]
    public async Task GroundAsync_ignores_high_similarity_block_from_a_different_book()
    {
        // Only a high-similarity block exists, but it is scoped to a different book than the query.
        await SeedBlockAsync(QueryVector, sourceBook: "dmg14", page: 100);

        var result = await _sut.GroundAsync(EntityText, "phb14", 100, CancellationToken.None);

        result.BelowFloor.Should().BeTrue();
        result.Score.Should().Be(0); // filtered out entirely -> no hits -> score treated as 0
    }

    [Fact]
    public async Task GroundAsync_ignores_high_similarity_block_outside_the_page_window()
    {
        // Query page 100 with a +/-2 window covers pages 98-102; page 200 is well outside it.
        await SeedBlockAsync(QueryVector, sourceBook: "phb14", page: 200);

        var result = await _sut.GroundAsync(EntityText, "phb14", 100, CancellationToken.None);

        result.BelowFloor.Should().BeTrue();
        result.Score.Should().Be(0);
    }

    [Fact]
    public async Task GroundAsync_matches_block_at_the_page_window_boundary()
    {
        // Query page 100, window +/-2 -> inclusive upper edge is page 102.
        await SeedBlockAsync(QueryVector, sourceBook: "phb14", page: 102);

        var result = await _sut.GroundAsync(EntityText, "phb14", 100, CancellationToken.None);

        result.BelowFloor.Should().BeFalse();
    }

    private async Task SeedBlockAsync(float[] vector, string sourceBook, int page)
    {
        var point = new PointStruct { Id = Guid.NewGuid(), Vectors = vector };
        point.Payload[QdrantPayloadFields.Text] = "seed block text";
        point.Payload[QdrantPayloadFields.SourceBook] = sourceBook;
        point.Payload[QdrantPayloadFields.PageNumber] = page;

        await _client.UpsertAsync(_collectionName, [point]);
    }
}
