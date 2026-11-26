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
/// A player's "item" is any carried thing: the model asks search_entities for type Item when it means a
/// magic item (staff of power, ring of spell storing), so the Item filter covers MagicItem, Weapon and Armor
/// on real Qdrant. Docker must be running.
/// </summary>
[Collection("qdrant")]
public sealed class ItemTypeFilterIntegrationTests : IAsyncLifetime
{
    private const int VectorSize = 4;
    private static readonly float[] FixedVector = [0.1f, 0.2f, 0.3f, 0.4f];

    private readonly QdrantFixture _fixture;
    private readonly string _collectionName = $"dnd_entities_item_type_test_{Guid.NewGuid():N}";
    private QdrantClient _client = null!;
    private EntityRetrievalService _sut = null!;

    public ItemTypeFilterIntegrationTests(QdrantFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        var grpc = new Uri(_fixture.Container.GetGrpcConnectionString());
        _client = new QdrantClient(grpc.Host, grpc.Port);
        await _client.CreateCollectionAsync(_collectionName, QdrantEntityVectorStore.VectorParams(VectorSize));
        await _client.CreatePayloadIndexAsync(_collectionName, EntityPayloadFields.Type, PayloadSchemaType.Keyword);

        var store = new QdrantEntityVectorStore(_client, Options.Create(new QdrantOptions { EntitiesCollectionName = _collectionName }));
        await store.UpsertAsync(
        [
            Make("test.item.staff", EntityType.Item, "Staff"),
            Make("test.magicitem.staff-of-power", EntityType.MagicItem, "Staff of Power"),
            Make("test.weapon.quarterstaff", EntityType.Weapon, "Quarterstaff"),
            Make("test.armor.leather", EntityType.Armor, "Leather"),
            Make("test.spell.shillelagh", EntityType.Spell, "Shillelagh"),
        ]);

        var rerankerOptions = Options.Create(new RerankerOptions { Enabled = false, RerankEntities = false });
        _sut = new EntityRetrievalService(
            new StubEmbeddingService(VectorSize, _ => FixedVector), store,
            Options.Create(new RetrievalOptions { MaxTopK = 50 }),
            new RerankingService(new StubReranker(), rerankerOptions), rerankerOptions,
            new SpellClassIndex("__no_5etools_dir__"));
    }

    public async Task DisposeAsync()
    {
        try { await _client.DeleteCollectionAsync(_collectionName); } catch { /* best-effort cleanup */ }
        _client.Dispose();
    }

    private static EntitySearchQuery Query(EntityType type) => new(
        QueryText: "staff", Type: type, SourceBook: null, Edition: null, BookType: null, SettingTag: null,
        Keyword: null, CrNumericLte: null, CrNumericGte: null, SpellLevel: null, DamageType: null, TopK: 10);

    [Fact]
    public async Task Item_search_also_finds_magic_items_weapons_and_armor()
    {
        var hits = await _sut.SearchAsync(Query(EntityType.Item), CancellationToken.None);

        hits.Select(h => h.Id).Should().BeEquivalentTo(
            ["test.item.staff", "test.magicitem.staff-of-power", "test.weapon.quarterstaff", "test.armor.leather"]);
    }

    [Fact]
    public async Task MagicItem_search_adds_another_type_only_when_the_query_names_it()
    {
        // "staff" names the plain Staff, so the type guess does not hide it; Quarterstaff is not named.
        var hits = await _sut.SearchAsync(Query(EntityType.MagicItem), CancellationToken.None);

        hits.Select(h => h.Id).Should().Equal("test.item.staff", "test.magicitem.staff-of-power");
    }

    private static EntityPoint Make(string id, EntityType type, string name)
    {
        using var doc = JsonDocument.Parse("{}");
        var envelope = new EntityEnvelope(
            Id: id, Type: type, Name: name, SourceBook: "DMG", Edition: "2014",
            Page: null, FirstAppearedIn: new FirstAppearance("DMG", "2014"),
            RevisedIn: [], SettingTags: [], CanonicalText: name, Fields: doc.RootElement.Clone(),
            DataSource: "", NeedsReview: false);
        return new EntityPoint(envelope, FixedVector, $"hash-{id}");
    }
}
