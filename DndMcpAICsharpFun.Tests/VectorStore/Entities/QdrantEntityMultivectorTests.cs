using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Qdrant.Client;

using Xunit;

namespace DndMcpAICsharpFun.Tests.VectorStore.Entities;

/// <summary>One point per entity, several vectors; the closest one decides (real Qdrant, Docker must be running).</summary>
[Collection("qdrant")]
public sealed class QdrantEntityMultivectorTests(QdrantFixture fixture)
{
    [Fact]
    public async Task The_best_vector_of_an_entity_decides_its_score_and_each_entity_is_one_point()
    {
        var grpc = new Uri(fixture.Container.GetGrpcConnectionString());
        using var client = new QdrantClient(grpc.Host, grpc.Port);
        var name = "dnd_entities_mv_" + Guid.NewGuid().ToString("N");
        await client.CreateCollectionAsync(name, QdrantEntityVectorStore.VectorParams(3));
        var store = new QdrantEntityVectorStore(client, Options.Create(new QdrantOptions { EntitiesCollectionName = name }));

        static EntityPoint Point(string id, params float[][] vectors) =>
            new(TestEnvelopes.Make(id, id, EntityType.Monster, "Edition2014", sourceBook: "MTF"), vectors, "hash");
        await store.UpsertAsync(
        [
            Point("mtf.monster.demogorgon", [1, 0, 0], [0, 0, 1]),
            Point("mtf.monster.other", [0.7f, 0.7f, 0]),
        ]);

        var hits = await store.SearchAsync([0, 0, 1], new EntityFilters(), 5);
        hits[0].Envelope.Id.Should().Be("mtf.monster.demogorgon");
        hits[0].Score.Should().BeApproximately(1f, 0.01f);
        (await client.CountAsync(name)).Should().Be(2);
    }
}