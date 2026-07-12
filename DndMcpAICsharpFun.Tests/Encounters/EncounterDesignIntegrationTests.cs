using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Encounters;
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

namespace DndMcpAICsharpFun.Tests.Encounters;

/// <summary>
/// Integration test against a REAL Qdrant instance (Testcontainers) proving that
/// <see cref="EncounterDesignService.BuildForUserAsync"/> and
/// <see cref="EncounterDesignService.RateForUserAsync"/> agree: rating the exact monster set a
/// build produced reports the very same <see cref="Difficulty"/> the build itself reported. This
/// exercises the real retrieval chain end to end — embedding, Qdrant filtered vector search (real
/// <see cref="QdrantEntityVectorStore"/>), CR parsing off the entity's raw <c>Fields</c> (mirroring
/// <see cref="EntitySearchMonsterSource"/>), and the shared XP/difficulty math — not fakes.
/// Party is supplied as explicit <c>partyLevels</c> so no Postgres/campaign is needed; ownership
/// of the campaign path is already covered by the real-Postgres <c>EncounterDesignServiceTests</c>.
/// Docker must be running for this test to execute.
/// </summary>
[Collection("qdrant")]
public sealed class EncounterDesignIntegrationTests : IAsyncLifetime
{
    private const int VectorSize = 4;
    private const string Edition = "Edition2014";

    // Fixed vector for every seeded monster and every query embedding: search relevance is
    // driven entirely by the real Qdrant payload filters (type/edition/CR range), not by vector
    // similarity, so a single deterministic vector keeps the whole chain reproducible.
    private static readonly float[] FixedVector = [0.1f, 0.2f, 0.3f, 0.4f];

    private readonly QdrantFixture _fixture;
    private readonly string _collectionName = $"dnd_entities_encounter_test_{Guid.NewGuid():N}";
    private QdrantClient _client = null!;
    private EncounterDesignService _sut = null!;

    public EncounterDesignIntegrationTests(QdrantFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var grpc = new Uri(_fixture.Container.GetGrpcConnectionString());
        _client = new QdrantClient(grpc.Host, grpc.Port);

        await _client.CreateCollectionAsync(
            _collectionName,
            new VectorParams { Size = VectorSize, Distance = Distance.Cosine });

        // Mirrors QdrantCollectionInitializer.CreateEntityPayloadIndexesAsync for the fields
        // EntitySearchMonsterSource's filter actually touches (type/edition/CR range), so the
        // real filter behaves exactly as it would in production.
        await _client.CreatePayloadIndexAsync(_collectionName, EntityPayloadFields.Type, PayloadSchemaType.Keyword);
        await _client.CreatePayloadIndexAsync(_collectionName, EntityPayloadFields.Edition, PayloadSchemaType.Keyword);
        await _client.CreatePayloadIndexAsync(_collectionName, EntityPayloadFields.CrNumeric, PayloadSchemaType.Float);

        var qdrantOptions = Options.Create(new QdrantOptions { EntitiesCollectionName = _collectionName });
        var store = new QdrantEntityVectorStore(_client, qdrantOptions);

        await SeedMonstersAsync(store);

        // Deterministic embedding stub — the real embedding call happens (proving the chain is
        // wired end to end) but always returns the same fixed vector, so the test never depends
        // on an actual embedding model.
        var embeddings = new StubEmbeddingService(VectorSize, _ => FixedVector);

        // Reranker fully disabled via the global kill-switch, so RerankingService never calls
        // into the (otherwise-unused) IReranker stub.
        var rerankerOptions = Options.Create(new RerankerOptions { Enabled = false, RerankEntities = false });
        var rerankingService = new RerankingService(new StubReranker(), rerankerOptions);
        var retrievalOptions = Options.Create(new RetrievalOptions { MaxTopK = 50 });

        IEntityRetrievalService search = new EntityRetrievalService(
            embeddings, store, retrievalOptions, rerankingService, rerankerOptions);

        var monsterSource = new EntitySearchMonsterSource(search);
        var assessor = new EncounterAssessor();
        var generator = new EncounterGenerator(monsterSource, assessor);

        // partyLevels is always supplied explicitly in this test's Fact, so
        // EncounterDesignService's private ResolvePartyAsync never touches the campaign/hero
        // repos — null! is safe here (no Postgres needed for this test's job: proving the real
        // Qdrant retrieval chain makes build and rate agree).
        _sut = new EncounterDesignService(assessor, generator, null!, null!, search);
    }

    public async Task DisposeAsync()
    {
        try { await _client.DeleteCollectionAsync(_collectionName); } catch { /* best-effort cleanup */ }
        _client.Dispose();
    }

    [Fact]
    public async Task BuildForUserAsync_and_RateForUserAsync_agree_over_real_qdrant_retrieval()
    {
        IReadOnlyList<int> partyLevels = [5, 5, 5, 5];

        var built = await _sut.BuildForUserAsync(
            userId: 1,
            campaignId: null,
            partyLevels,
            Difficulty.Hard,
            DndVersion.Edition2014,
            theme: null,
            crLte: null,
            crGte: null,
            CancellationToken.None);

        // Non-vacuity: the build actually pulled real monsters back out of Qdrant, not an empty
        // set — every monster's id is one this test seeded, proving the source of the data.
        built.Assessment.Monsters.Should().NotBeEmpty();
        built.Assessment.Monsters.Should().OnlyContain(m => m.Id.StartsWith("test.monster.", StringComparison.Ordinal));

        // Build==rate: rate the exact same real-retrieved monster set the build produced (by
        // id, resolved again through the real store's GetByIdAsync) and assert the whole real
        // chain — Qdrant search -> CR resolve -> assess — is internally consistent.
        var rated = await _sut.RateForUserAsync(
            userId: 1,
            campaignId: null,
            partyLevels,
            monsters: built.Assessment.Monsters.Select(m => new MonsterQuantity(m.Id, 1)).ToList(),
            DndVersion.Edition2014,
            CancellationToken.None);

        rated.Difficulty.Should().Be(built.Assessment.Difficulty);
        rated.TotalMonsterXp.Should().Be(built.Assessment.TotalMonsterXp);
        rated.AdjustedXp.Should().Be(built.Assessment.AdjustedXp);

        // With 4x CR-3 (700xp each) and 1x CR-5 (1800xp) seeded within the generator's default CR
        // band for a Hard target and a 4x level-5 party ([0.125, 7] — derived from the Hard
        // budget of 3000 XP, whose highest CR at or under is CR 7 @ 2900 XP), the greedy builder
        // reaches Hard exactly (CR-5 first, then one CR-3 pushes the 2014 monster-count-adjusted
        // total into the Hard band) without ever needing to overshoot — so FullyMatched is
        // honestly true.
        built.Assessment.Difficulty.Should().Be(Difficulty.Hard);
        built.FullyMatched.Should().BeTrue();
    }

    private async Task SeedMonstersAsync(QdrantEntityVectorStore store)
    {
        var points = new List<EntityPoint>
        {
            MakeMonster("test.monster.bandit-1", "Test Bandit 1", cr: "3"),
            MakeMonster("test.monster.bandit-2", "Test Bandit 2", cr: "3"),
            MakeMonster("test.monster.bandit-3", "Test Bandit 3", cr: "3"),
            MakeMonster("test.monster.bandit-4", "Test Bandit 4", cr: "3"),
            MakeMonster("test.monster.ogre-boss", "Test Ogre Boss", cr: "5"),
        };
        await store.UpsertAsync(points);
    }

    private static EntityPoint MakeMonster(string id, string name, string cr)
    {
        using var doc = JsonDocument.Parse($$"""{"cr":"{{cr}}"}""");
        var envelope = new EntityEnvelope(
            Id: id,
            Type: EntityType.Monster,
            Name: name,
            SourceBook: "TestBook",
            Edition: Edition,
            Page: null,
            FirstAppearedIn: new FirstAppearance("TestBook", Edition),
            RevisedIn: [],
            SettingTags: [],
            CanonicalText: $"{name}, a test monster of CR {cr}.",
            Fields: doc.RootElement.Clone());
        return new EntityPoint(envelope, FixedVector, FileHash: "test-hash");
    }
}