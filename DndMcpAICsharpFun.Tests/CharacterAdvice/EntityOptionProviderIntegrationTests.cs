using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.CharacterAdvice;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Tests.TestDoubles;
using DndMcpAICsharpFun.Tests.VectorStore.Entities;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Qdrant.Client;
using Qdrant.Client.Grpc;

using Xunit;

namespace DndMcpAICsharpFun.Tests.CharacterAdvice;

/// <summary>
/// Integration test against a REAL Qdrant instance (Testcontainers) proving that
/// <see cref="EntityOptionProvider"/> returns real, cited entities from <c>dnd_entities</c> and
/// that its subclass post-filter genuinely narrows to the requested class — not just that a
/// query executed. Exercises the real retrieval chain end to end (embedding stub, Qdrant filtered
/// vector search via the real <see cref="QdrantEntityVectorStore"/>, and the <c>Fields</c>
/// deserialization the provider's post-filter depends on). Docker must be running.
/// </summary>
[Collection("qdrant")]
public sealed class EntityOptionProviderIntegrationTests : IAsyncLifetime
{
    private const int VectorSize = 4;
    private const string Edition = "2014";

    // Fixed vector for every seeded entity and every query embedding: relevance here is driven
    // entirely by the real Qdrant payload filters (type/edition), not vector similarity, so a
    // single deterministic vector keeps the chain reproducible.
    private static readonly float[] FixedVector = [0.1f, 0.2f, 0.3f, 0.4f];

    private readonly QdrantFixture _fixture;
    private readonly string _collectionName = $"dnd_entities_option_provider_test_{Guid.NewGuid():N}";
    private QdrantClient _client = null!;
    private EntityOptionProvider _sut = null!;

    public EntityOptionProviderIntegrationTests(QdrantFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var grpc = new Uri(_fixture.Container.GetGrpcConnectionString());
        _client = new QdrantClient(grpc.Host, grpc.Port);

        await _client.CreateCollectionAsync(
            _collectionName,
            new VectorParams { Size = VectorSize, Distance = Distance.Cosine });

        // Mirrors QdrantCollectionInitializer.CreateEntityPayloadIndexesAsync for the fields this
        // test's filters actually touch (type/edition), so the real filter behaves exactly as it
        // would in production.
        await _client.CreatePayloadIndexAsync(_collectionName, EntityPayloadFields.Type, PayloadSchemaType.Keyword);
        await _client.CreatePayloadIndexAsync(_collectionName, EntityPayloadFields.Edition, PayloadSchemaType.Keyword);

        var qdrantOptions = Options.Create(new QdrantOptions { EntitiesCollectionName = _collectionName });
        var store = new QdrantEntityVectorStore(_client, qdrantOptions);

        await SeedEntitiesAsync(store);

        // Deterministic embedding stub — the real embedding call happens (proving the chain is
        // wired end to end) but always returns the same fixed vector, so the test never depends
        // on an actual embedding model.
        var embeddings = new StubEmbeddingService(VectorSize, _ => FixedVector);

        // Reranker fully disabled via the global kill-switch.
        var rerankerOptions = Options.Create(new RerankerOptions { Enabled = false, RerankEntities = false });
        var rerankingService = new RerankingService(new StubReranker(), rerankerOptions);
        var retrievalOptions = Options.Create(new RetrievalOptions { MaxTopK = 50 });

        IEntityRetrievalService retrieval = new EntityRetrievalService(
            embeddings, store, retrievalOptions, rerankingService, rerankerOptions,
            new DndMcpAICsharpFun.Features.Retrieval.Entities.SpellClassIndex("__no_5etools_dir__"));

        _sut = new EntityOptionProvider(retrieval);
    }

    public async Task DisposeAsync()
    {
        try { await _client.DeleteCollectionAsync(_collectionName); } catch { /* best-effort cleanup */ }
        _client.Dispose();
    }

    [Fact]
    public async Task SubclassOptions_returns_only_the_requested_classs_subclasses_with_citations()
    {
        var fighterOptions = await _sut.SubclassOptions("Fighter", Edition, CancellationToken.None);

        fighterOptions.Should().HaveCount(2);
        fighterOptions.Select(o => o.Id).Should().BeEquivalentTo(
            ["test.subclass.champion", "test.subclass.battle-master"]);
        fighterOptions.Should().OnlyContain(o =>
            !string.IsNullOrEmpty(o.Id) && !string.IsNullOrEmpty(o.Name) && !string.IsNullOrEmpty(o.Source));

        // Non-vacuity: the class-name post-filter genuinely excludes other classes' subclasses,
        // not just an artifact of an empty/near-empty seed set.
        fighterOptions.Should().NotContain(o => o.Id == "test.subclass.evocation");
    }

    [Fact]
    public async Task SubclassOptions_non_vacuity_wizard_query_returns_wizard_not_fighter_subclasses()
    {
        var wizardOptions = await _sut.SubclassOptions("Wizard", Edition, CancellationToken.None);

        wizardOptions.Should().ContainSingle(o => o.Id == "test.subclass.evocation");
        wizardOptions.Should().NotContain(o =>
            o.Id == "test.subclass.champion" || o.Id == "test.subclass.battle-master");
    }

    [Fact]
    public async Task FeatOptions_includes_the_seeded_feats_with_citations()
    {
        var feats = await _sut.FeatOptions(Edition, CancellationToken.None);

        feats.Select(o => o.Id).Should().Contain(["test.feat.alert", "test.feat.tough"]);
        feats.Should().OnlyContain(o =>
            !string.IsNullOrEmpty(o.Id) && !string.IsNullOrEmpty(o.Name) && !string.IsNullOrEmpty(o.Source));
    }

    private async Task SeedEntitiesAsync(QdrantEntityVectorStore store)
    {
        var points = new List<EntityPoint>
        {
            MakeSubclass("test.subclass.champion", "Champion", className: "Fighter"),
            MakeSubclass("test.subclass.battle-master", "Battle Master", className: "Fighter"),
            MakeSubclass("test.subclass.evocation", "School of Evocation", className: "Wizard"),
            MakeFeat("test.feat.alert", "Alert"),
            MakeFeat("test.feat.tough", "Tough"),
        };
        await store.UpsertAsync(points);
    }

    private static EntityPoint MakeSubclass(string id, string name, string className)
    {
        using var doc = JsonDocument.Parse($$"""{"className":"{{className}}"}""");
        var envelope = new EntityEnvelope(
            Id: id,
            Type: EntityType.Subclass,
            Name: name,
            SourceBook: "TestBook",
            Edition: Edition,
            Page: null,
            FirstAppearedIn: new FirstAppearance("TestBook", Edition),
            RevisedIn: [],
            SettingTags: [],
            CanonicalText: $"{name}, a subclass of {className}.",
            Fields: doc.RootElement.Clone());
        return new EntityPoint(envelope, FixedVector, FileHash: "test-hash");
    }

    private static EntityPoint MakeFeat(string id, string name)
    {
        using var doc = JsonDocument.Parse("""{"description":"A test feat.","prerequisites":[],"grants":[]}""");
        var envelope = new EntityEnvelope(
            Id: id,
            Type: EntityType.Feat,
            Name: name,
            SourceBook: "TestBook",
            Edition: Edition,
            Page: null,
            FirstAppearedIn: new FirstAppearance("TestBook", Edition),
            RevisedIn: [],
            SettingTags: [],
            CanonicalText: $"{name}, a test feat.",
            Fields: doc.RootElement.Clone());
        return new EntityPoint(envelope, FixedVector, FileHash: "test-hash");
    }
}
