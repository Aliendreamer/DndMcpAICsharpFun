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

/// <summary>Real-Qdrant (Testcontainers) check of the stale-point purge filter. Docker must be running.</summary>
[Collection("qdrant")]
public sealed class QdrantEntityVectorStoreStalePurgeTests(QdrantFixture fixture) : IAsyncLifetime
{
    private const string CollectionName = "dnd_entities_stale_purge_test";
    private QdrantClient _client = null!;
    private QdrantEntityVectorStore _store = null!;

    public async Task InitializeAsync()
    {
        var grpc = new Uri(fixture.Container.GetGrpcConnectionString());
        _client = new QdrantClient(grpc.Host, grpc.Port);
        if (!await _client.CollectionExistsAsync(CollectionName))
            await _client.CreateCollectionAsync(CollectionName, new VectorParams { Size = 4, Distance = Distance.Cosine });
        _store = new QdrantEntityVectorStore(_client, Options.Create(new QdrantOptions { EntitiesCollectionName = CollectionName }));
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DeleteStaleForBookAsync_removes_only_the_books_points_not_written_by_the_current_run()
    {
        static EntityPoint P(string id, string book, string ds, string hash) => new(
            TestEnvelopes.Make(id, id, EntityType.Spell, "Edition2014", sourceBook: book, dataSource: ds), Vector(1), hash);
        await _store.UpsertAsync([
            P("tce.class.primal-paths", "TCE", "llm", ""),
            P("tce.spell.old", "TCE", "programmatic", "oldhash"),
            P("tce.spell.new", "TCE", "programmatic", "newhash"),
            P("phb14.spell.fireball", "PHB", "llm", ""),
        ]);

        await _store.DeleteStaleForBookAsync("TCE", "newhash");

        var left = await _store.ScrollAllAsync();
        left.Select(h => h.Envelope.Id).Should().BeEquivalentTo(["tce.spell.new", "phb14.spell.fireball"]);
    }

    private static float[] Vector(int seed) =>
        Enumerable.Range(0, 4).Select(i => (float)(seed + i) / 10f).ToArray();
}
