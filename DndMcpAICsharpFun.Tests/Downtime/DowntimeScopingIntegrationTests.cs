using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Downtime;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Tests.TestDoubles;
using DndMcpAICsharpFun.Tests.VectorStore.Entities;

using FluentAssertions;

using Qdrant.Client;
using Qdrant.Client.Grpc;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Downtime;

/// <summary>
/// Integration test proving the downtime-scoping OR-filter (<c>DowntimeSources.Keys</c> ->
/// <c>RetrievalQuery.SourceKeys</c> -> <c>RagRetrievalService</c>'s <c>anyBook</c> filter) is REAL
/// against a genuine Qdrant <c>dnd_blocks</c> collection (Testcontainers) — not merely that a
/// <see cref="Filter"/> object is constructed correctly (that is already covered by the fake-based
/// <see cref="DowntimeServiceTests"/>). Both seeded blocks are given the IDENTICAL embedding
/// vector, so vector similarity alone would rank them equally and return both; only the SourceKeys
/// scoping filter (matched against the stable <c>source_key</c> payload, not the display-name
/// <c>source_book</c> payload) can exclude the off-scope Monster Manual block (key "MM" is NOT in
/// <see cref="DowntimeSources.Keys"/> — only "XGE" and "DMG" are). This is the discrimination gate
/// for the whole scoping mechanism: if the filter were ever accidentally reverted to match on
/// <c>source_book</c> instead of <c>source_key</c>, this test would go red by picking up the Monster
/// Manual passage (it carries a real <c>source_book</c> display name too). Docker is required for the
/// Qdrant Testcontainer.
/// </summary>
public sealed class DowntimeScopingIntegrationTests : IClassFixture<QdrantFixture>, IAsyncLifetime
{
    private const int VectorSize = 4;
    private static readonly float[] SharedVector = [1f, 0f, 0f, 0f];

    private readonly QdrantFixture _qdrantFixture;
    private readonly string _collectionName = $"dnd_blocks_downtime_scoping_test_{Guid.NewGuid():N}";
    private QdrantClient _client = null!;

    public DowntimeScopingIntegrationTests(QdrantFixture qdrantFixture)
    {
        _qdrantFixture = qdrantFixture;
    }

    public async Task InitializeAsync()
    {
        var grpc = new Uri(_qdrantFixture.Container.GetGrpcConnectionString());
        _client = new QdrantClient(grpc.Host, grpc.Port);

        await _client.CreateCollectionAsync(
            _collectionName,
            new VectorParams { Size = VectorSize, Distance = Distance.Cosine });
        await _client.CreatePayloadIndexAsync(_collectionName, QdrantPayloadFields.SourceKey, PayloadSchemaType.Keyword);
    }

    public async Task DisposeAsync()
    {
        try { await _client.DeleteCollectionAsync(_collectionName); } catch { /* best-effort cleanup */ }
        _client.Dispose();
    }

    private async Task SeedBlockAsync(string text, string sourceKey)
    {
        // Every seeded block shares SharedVector, so the ONLY thing that can distinguish them at
        // query time is the SourceKeys scoping filter — proving the filter, not vector ranking,
        // is what keeps the off-scope Monster Manual block out. source_book is also populated (its
        // real display name) to prove scoping does NOT accidentally key off it.
        var displayName = BookCatalog.KeyToDisplayName.GetValueOrDefault(sourceKey, sourceKey);
        var point = new PointStruct { Id = Guid.NewGuid(), Vectors = SharedVector };
        point.Payload[QdrantPayloadFields.Text] = text;
        point.Payload[QdrantPayloadFields.SourceBook] = displayName;
        point.Payload[QdrantPayloadFields.SourceKey] = sourceKey;
        point.Payload[QdrantPayloadFields.Version] = DndVersion.Edition2014.ToString();
        point.Payload[QdrantPayloadFields.Category] = ContentCategory.Rule.ToString();
        point.Payload[QdrantPayloadFields.Chapter] = "Downtime";
        point.Payload[QdrantPayloadFields.PageNumber] = 1L;
        point.Payload[QdrantPayloadFields.ChunkIndex] = 0L;

        await _client.UpsertAsync(_collectionName, [point]);
    }

    [Fact]
    public async Task Scoping_returns_downtime_blocks_and_excludes_off_scope_blocks()
    {
        await SeedBlockAsync(
            "Crafting: a character can craft nonmagical items, spending downtime days and gold based on the item's price.",
            "XGE"); // in-scope: XGE is in DowntimeSources.Keys
        await SeedBlockAsync(
            "Yuan-ti Anathema. Huge monstrosity, lawful evil. Armor Class 16, Hit Points 154.",
            "MM"); // off-scope: MM is NOT in DowntimeSources.Keys, must be excluded

        var qdrantOptions = Options.Create(new QdrantOptions
        {
            BlocksCollectionName = _collectionName,
            VectorSize = VectorSize,
            Quantization = new QdrantQuantizationOptions { Enabled = false },
        });
        var embeddings = new StubEmbeddingService(VectorSize, _ => SharedVector);
        var searchClient = new QdrantSearchClientAdapter(_client, qdrantOptions);
        var reranker = Substitute.For<IReranker>();
        reranker.Enabled.Returns(false);
        var rerankerOptions = Options.Create(new RerankerOptions { Enabled = false });
        var rerankingService = new RerankingService(reranker, rerankerOptions);

        var rag = new RagRetrievalService(
            searchClient,
            embeddings,
            qdrantOptions,
            Options.Create(new RetrievalOptions()),
            new QdrantSparseState { SparseSupported = false },
            rerankingService,
            rerankerOptions);

        var svc = new DowntimeService(rag);

        var result = await svc.PlanAsync("crafting", edition: null, CancellationToken.None);

        result.Passages.Select(p => p.SourceBook).Should().Contain("Xanathar's Guide to Everything");
        result.Passages.Select(p => p.SourceBook).Should().NotContain("Monster Manual 2014"); // scoping is real
    }
}
