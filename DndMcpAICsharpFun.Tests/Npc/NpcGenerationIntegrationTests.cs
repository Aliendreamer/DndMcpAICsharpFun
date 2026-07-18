using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Npc;
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

namespace DndMcpAICsharpFun.Tests.Npc;

/// <summary>
/// Integration test against a REAL Qdrant instance (Testcontainers) proving that
/// <see cref="NpcGenerationService"/> grounds an archetype to a genuine seeded Monster entity
/// end to end — embedding stub, Qdrant filtered vector search via the real
/// <see cref="QdrantEntityVectorStore"/>, and the real <see cref="EntityRetrievalService"/> — not
/// just that a substitute returned canned data. Mirrors
/// <c>DndMcpAICsharpFun.Tests.CharacterAdvice.EntityOptionProviderIntegrationTests</c>'s
/// Testcontainers-per-collection pattern. Docker must be running.
/// </summary>
[Collection("qdrant")]
public sealed class NpcGenerationIntegrationTests : IAsyncLifetime
{
    private const int VectorSize = 4;

    // Fixed vector for the seeded entity and every query embedding: relevance is driven entirely
    // by the real Qdrant payload filter (Type=Monster), not vector similarity, so a single
    // deterministic vector keeps the chain reproducible.
    private static readonly float[] FixedVector = [0.1f, 0.2f, 0.3f, 0.4f];

    private readonly QdrantFixture _fixture;
    private readonly string _collectionName = $"dnd_entities_npc_generation_test_{Guid.NewGuid():N}";
    private QdrantClient _client = null!;
    private NpcGenerationService _sut = null!;

    public NpcGenerationIntegrationTests(QdrantFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var grpc = new Uri(_fixture.Container.GetGrpcConnectionString());
        _client = new QdrantClient(grpc.Host, grpc.Port);

        await _client.CreateCollectionAsync(
            _collectionName,
            new VectorParams { Size = VectorSize, Distance = Distance.Cosine });

        // Mirrors QdrantCollectionInitializer.CreateEntityPayloadIndexesAsync for the field this
        // test's filter actually touches (Type), so the real filter behaves exactly as it would
        // in production.
        await _client.CreatePayloadIndexAsync(_collectionName, EntityPayloadFields.Type, PayloadSchemaType.Keyword);
        await _client.CreatePayloadIndexAsync(_collectionName, EntityPayloadFields.Edition, PayloadSchemaType.Keyword);

        var qdrantOptions = Options.Create(new QdrantOptions { EntitiesCollectionName = _collectionName });
        var store = new QdrantEntityVectorStore(_client, qdrantOptions);

        await store.UpsertAsync([MakeSpyMonster()]);

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

        _sut = new NpcGenerationService(retrieval);
    }

    public async Task DisposeAsync()
    {
        try { await _client.DeleteCollectionAsync(_collectionName); } catch { /* best-effort cleanup */ }
        _client.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_grounds_a_known_archetype_to_the_real_seeded_stat_block()
    {
        var npc = await _sut.GenerateAsync("a shifty dockworker", "Spy", maxCr: null, CancellationToken.None);

        npc.ArchetypeInCorpus.Should().BeTrue();
        npc.StatBlock.Should().NotBeNull();
        npc.StatBlock!.Name.Should().Be("Spy");
        npc.StatBlock.Cr.Should().Be(1);
        npc.StatBlock.Hp.Should().Be(27);
        npc.StatBlock.Dex.Should().Be(15);
        npc.StatBlock.CanonicalText.Should().NotBeNullOrWhiteSpace();
        npc.StatBlock.CanonicalText.Should().Contain("Spy");
    }

    [Fact]
    public async Task GenerateAsync_bogus_archetype_returns_not_in_corpus_with_roster_and_no_block()
    {
        var npc = await _sut.GenerateAsync("x", "Nonexistent Archetype", maxCr: null, CancellationToken.None);

        npc.ArchetypeInCorpus.Should().BeFalse();
        npc.StatBlock.Should().BeNull();
        npc.AvailableArchetypes.Should().NotBeEmpty();
        npc.AvailableArchetypes.Should().BeEquivalentTo(NpcArchetypes.Common);
    }

    private static EntityPoint MakeSpyMonster()
    {
        const string canonicalText =
            "Spy\nMedium humanoid, any race\nAC 12\nHP 27 (6d8)\nSpeed 30 ft.\n" +
            "STR 10 DEX 15 CON 10 INT 12 WIS 14 CHA 16\nSkills Deception +3, Insight +4, " +
            "Persuasion +5, Stealth +4\nSenses passive Perception 12\nLanguages any two languages\n" +
            "Challenge 1 (200 XP)";
        using var doc = JsonDocument.Parse(
            """{"cr":"1","hp":{"average":27},"str":10,"dex":15,"con":10,"int":12,"wis":14,"cha":16}""");

        var envelope = new EntityEnvelope(
            Id: "test.monster.spy",
            Type: EntityType.Monster,
            Name: "Spy",
            SourceBook: "MM",
            Edition: "2014",
            Page: 349,
            FirstAppearedIn: new FirstAppearance("MM", "2014"),
            RevisedIn: [],
            SettingTags: [],
            CanonicalText: canonicalText,
            Fields: doc.RootElement.Clone());
        return new EntityPoint(envelope, FixedVector, FileHash: "test-hash");
    }
}
