using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Xunit;

namespace DndMcpAICsharpFun.Tests.VectorStore.Entities;

/// <summary>
/// Integration test against a real Qdrant instance (Testcontainers) covering the
/// full-corpus scroll and delete-by-ids operations added for corpus-wide entity dedup.
/// Docker must be running for this test to execute.
/// </summary>
[Collection("qdrant")]
public sealed class QdrantEntityVectorStoreScrollTests : IAsyncLifetime
{
    private const string CollectionName = "dnd_entities_scroll_test";

    private readonly QdrantFixture _fixture;
    private QdrantClient _client = null!;
    private QdrantEntityVectorStore _store = null!;

    public QdrantEntityVectorStoreScrollTests(QdrantFixture fixture) => _fixture = fixture;

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

        var options = Options.Create(new QdrantOptions { EntitiesCollectionName = CollectionName });
        _store = new QdrantEntityVectorStore(_client, options);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ScrollAllAsync_returns_every_point_and_DeleteByIdsAsync_removes_only_the_targeted_ones()
    {
        var fireballCore = new EntityPoint(
            TestEnvelopes.Make("phb14.spell.fireball", "Fireball", EntityType.Spell, "Edition2014", sourceBook: "PHB"),
            Vector(1), "hash-phb");
        var fireballDuplicate = new EntityPoint(
            TestEnvelopes.Make("xge.spell.fireball", "Fireball", EntityType.Spell, "Edition2014", sourceBook: "XGE"),
            Vector(2), "hash-xge");
        var iceKnife = new EntityPoint(
            TestEnvelopes.Make("phb14.spell.ice-knife", "Ice Knife", EntityType.Spell, "Edition2014", sourceBook: "PHB"),
            Vector(3), "hash-phb");

        await _store.UpsertAsync([fireballCore, fireballDuplicate, iceKnife]);

        var all = await _store.ScrollAllAsync();
        all.Should().HaveCount(3);
        all.Select(h => h.Envelope.Id).Should().BeEquivalentTo(
        [
            fireballCore.Envelope.Id,
            fireballDuplicate.Envelope.Id,
            iceKnife.Envelope.Id,
        ]);
        all.Should().OnlyContain(h => h.Score == 0f);

        await _store.DeleteByIdsAsync([fireballDuplicate.Envelope.Id]);

        (await _store.GetByIdAsync(fireballDuplicate.Envelope.Id)).Should().BeNull();

        var remaining = await _store.ScrollAllAsync();
        remaining.Should().HaveCount(2);
        remaining.Select(h => h.Envelope.Id).Should().BeEquivalentTo(
        [
            fireballCore.Envelope.Id,
            iceKnife.Envelope.Id,
        ]);
    }

    private static float[] Vector(int seed) =>
        Enumerable.Range(0, 4).Select(i => (float)(seed + i) / 10f).ToArray();
}
