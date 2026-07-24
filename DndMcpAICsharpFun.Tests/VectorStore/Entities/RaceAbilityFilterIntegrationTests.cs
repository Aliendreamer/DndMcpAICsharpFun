using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Qdrant.Client;
using Qdrant.Client.Grpc;

using Xunit;

namespace DndMcpAICsharpFun.Tests.VectorStore.Entities;

/// <summary>
/// Integration test against a REAL Qdrant instance (Testcontainers) proving that the
/// race-ability-filter (<see cref="EntityRetrievalService.ListAsync"/> with
/// <see cref="EntitySearchQuery.AbilityBonus"/> set) works end to end on real infra: a race's
/// <c>Fields</c> (real 5etools ability JSON shapes) round-trips through Qdrant's payload
/// (<see cref="QdrantEntityVectorStore"/>'s <c>FieldsJson</c> column) onto the list hit, and
/// <see cref="RaceAbilityParser"/> correctly matches both fixed and choosable ability bonuses
/// against it. Mirrors <c>DndMcpAICsharpFun.Tests.CharacterAdvice.EntityOptionProviderIntegrationTests</c>'s
/// Testcontainers-per-collection pattern. Docker must be running.
/// </summary>
[Collection("qdrant")]
public sealed class RaceAbilityFilterIntegrationTests : IAsyncLifetime
{
    private const int VectorSize = 4;
    private const string Edition = "2014";

    // Fixed vector for every seeded entity and every query embedding: relevance here is driven
    // entirely by the real query-time scroll-then-filter (race-ability-filter never touches
    // vector similarity), so a single deterministic vector keeps the chain reproducible.
    private static readonly float[] FixedVector = [0.1f, 0.2f, 0.3f, 0.4f];

    private readonly QdrantFixture _fixture;
    private readonly string _collectionName = $"dnd_entities_race_ability_test_{Guid.NewGuid():N}";
    private QdrantClient _client = null!;
    private EntityRetrievalService _sut = null!;

    public RaceAbilityFilterIntegrationTests(QdrantFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var grpc = new Uri(_fixture.Container.GetGrpcConnectionString());
        _client = new QdrantClient(grpc.Host, grpc.Port);

        await _client.CreateCollectionAsync(
            _collectionName,
            new VectorParams { Size = VectorSize, Distance = Distance.Cosine });

        // Mirrors QdrantCollectionInitializer.CreateEntityPayloadIndexesAsync for the field this
        // test's forced Race filter actually touches (Type), so the real filter behaves exactly
        // as it would in production.
        await _client.CreatePayloadIndexAsync(_collectionName, EntityPayloadFields.Type, PayloadSchemaType.Keyword);

        var qdrantOptions = Options.Create(new QdrantOptions { EntitiesCollectionName = _collectionName });
        var store = new QdrantEntityVectorStore(_client, qdrantOptions);

        await store.UpsertAsync(
        [
            MakeRace("test.race.fixed-str", "Fixed Str Race", """{"ability":[{"str":2}]}"""),
            MakeRace("test.race.choose-str-dex", "Choose Str Or Dex Race", """{"ability":[{"choose":{"from":["str","dex"]}}]}"""),
            MakeRace("test.race.dex-only", "Dex Only Race", """{"ability":[{"dex":2}]}"""),
        ]);

        // Deterministic embedding stub — the real embedding call would happen for a vector search,
        // but the race-ability-filter path never calls it (it's a query-time scroll-then-filter),
        // so this only guards against a future regression that routes AbilityBonus through search.
        var embeddings = new StubEmbeddingService(VectorSize, _ => FixedVector);

        // Reranker fully disabled via the global kill-switch.
        var rerankerOptions = Options.Create(new RerankerOptions { Enabled = false, RerankEntities = false });
        var rerankingService = new RerankingService(new StubReranker(), rerankerOptions);
        var retrievalOptions = Options.Create(new RetrievalOptions { MaxTopK = 50 });

        _sut = new EntityRetrievalService(
            embeddings, store, retrievalOptions, rerankingService, rerankerOptions,
            new SpellClassIndex("__no_5etools_dir__"));
    }

    public async Task DisposeAsync()
    {
        try { await _client.DeleteCollectionAsync(_collectionName); } catch { /* best-effort cleanup */ }
        _client.Dispose();
    }

    [Fact]
    public async Task ListAsync_with_AbilityBonus_str_returns_only_the_fixed_and_choose_str_races()
    {
        var query = new EntitySearchQuery(
            QueryText: "", Type: null, SourceBook: null, Edition: null, BookType: null,
            SettingTag: null, Keyword: null, CrNumericLte: null, CrNumericGte: null,
            SpellLevel: null, DamageType: null, TopK: 10, AbilityBonus: "str");

        var result = await _sut.ListAsync(query, cap: 50, CancellationToken.None);

        result.Total.Should().Be(2);
        result.Rows.Select(r => r.Id).Should().BeEquivalentTo(
        [
            "test.race.fixed-str",
            "test.race.choose-str-dex",
        ]);
        result.Rows.Select(r => r.Id).Should().NotContain("test.race.dex-only");
    }

    private static EntityPoint MakeRace(string id, string name, string fieldsJson)
    {
        using var doc = JsonDocument.Parse(fieldsJson);
        var envelope = new EntityEnvelope(
            Id: id, Type: EntityType.Race, Name: name, SourceBook: "PHB", Edition: Edition,
            Page: null, FirstAppearedIn: new FirstAppearance("PHB", Edition),
            RevisedIn: [], SettingTags: [], CanonicalText: name, Fields: doc.RootElement.Clone(),
            DataSource: "", NeedsReview: false);
        return new EntityPoint(envelope, FixedVector, $"hash-{id}");
    }
}