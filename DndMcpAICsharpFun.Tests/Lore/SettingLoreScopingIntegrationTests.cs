using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Tests.Persistence;
using DndMcpAICsharpFun.Tests.TestDoubles;
using DndMcpAICsharpFun.Tests.VectorStore.Entities;

using FluentAssertions;

using Qdrant.Client;
using Qdrant.Client.Grpc;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Lore;

/// <summary>
/// Integration test proving the setting-scoping OR-filter (<c>SettingCatalog.Resolve</c> ->
/// <c>RetrievalQuery.SourceKeys</c> -> <c>RagRetrievalService</c>'s <c>anyBook</c> filter) is REAL
/// against a genuine Qdrant <c>dnd_blocks</c> collection (Testcontainers) — not merely that a
/// <see cref="Filter"/> object is constructed correctly (that is already covered by the fake-based
/// <see cref="SettingLoreServiceTests"/>). Both seeded blocks are given the IDENTICAL embedding
/// vector, so vector similarity alone would rank them equally and return both; only the SourceKeys
/// scoping filter (matched against the stable <c>source_key</c> payload, not the display-name
/// <c>source_book</c> payload) can exclude the off-setting (VGM) block. This is the discrimination
/// gate for the whole scoping mechanism: if the filter were ever accidentally reverted to match on
/// <c>source_book</c> instead of <c>source_key</c>, this test would go red by picking up the VGM
/// passage (seeded blocks carry a real <c>source_book</c> display name but scoping must ignore it).
/// Docker is required for both the Qdrant and Postgres (<see cref="CampaignRepository"/> ownership
/// check) Testcontainers.
/// </summary>
public sealed class SettingLoreScopingIntegrationTests :
    IClassFixture<QdrantFixture>, IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const int VectorSize = 4;
    private static readonly float[] SharedVector = [1f, 0f, 0f, 0f];

    private readonly QdrantFixture _qdrantFixture;
    private readonly PostgresFixture _pgFixture;
    private readonly string _collectionName = $"dnd_blocks_setting_lore_test_{Guid.NewGuid():N}";
    private QdrantClient _client = null!;

    public SettingLoreScopingIntegrationTests(QdrantFixture qdrantFixture, PostgresFixture pgFixture)
    {
        _qdrantFixture = qdrantFixture;
        _pgFixture = pgFixture;
    }

    public async Task InitializeAsync()
    {
        await _pgFixture.ResetAsync();

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
        // is what keeps the off-setting block out. source_book is also populated (a real display
        // name where one exists) to prove scoping does NOT accidentally key off it.
        var displayName = BookCatalog.KeyToDisplayName.GetValueOrDefault(sourceKey, sourceKey);
        var point = new PointStruct { Id = Guid.NewGuid(), Vectors = SharedVector };
        point.Payload[QdrantPayloadFields.Text] = text;
        point.Payload[QdrantPayloadFields.SourceBook] = displayName;
        point.Payload[QdrantPayloadFields.SourceKey] = sourceKey;
        point.Payload[QdrantPayloadFields.Version] = DndVersion.Edition2014.ToString();
        point.Payload[QdrantPayloadFields.Category] = ContentCategory.Lore.ToString();
        point.Payload[QdrantPayloadFields.Chapter] = "Chapter 1";
        point.Payload[QdrantPayloadFields.PageNumber] = 1L;
        point.Payload[QdrantPayloadFields.ChunkIndex] = 0L;

        await _client.UpsertAsync(_collectionName, [point]);
    }

    [Fact]
    public async Task Scoping_returns_in_setting_blocks_and_excludes_off_setting_blocks()
    {
        await SeedBlockAsync("The Dragonmarked Houses rule commerce.", "ERLW");   // in-setting: Eberron -> ERLW
        await SeedBlockAsync("Volo describes the beholder.", "VGM");              // off-setting: must be excluded

        var campaigns = new CampaignRepository(new TestDb(_pgFixture));
        var campaignId = await campaigns.CreateAsync(1, "Eberron Game", "d", setting: "Eberron");

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

        var svc = new SettingLoreService(campaigns, rag);

        var result = await svc.AskForUserAsync(1, campaignId, "who holds power", version: null, CancellationToken.None);

        result.Passages.Select(p => p.SourceBook).Should().Contain("Eberron: Rising from the Last War");
        result.Passages.Select(p => p.SourceBook).Should().NotContain("VGM"); // non-vacuity: scoping is real
    }
}
