using Testcontainers.Qdrant;

using Xunit;

namespace DndMcpAICsharpFun.Tests.VectorStore.Entities;

/// <summary>
/// Starts one Qdrant container shared across the "qdrant" test collection.
/// Mirrors <c>DndMcpAICsharpFun.Tests.Persistence.PostgresFixture</c>'s
/// Testcontainers-per-collection pattern for the entity vector store's own
/// integration tests.
/// </summary>
public sealed class QdrantFixture : IAsyncLifetime
{
    public QdrantContainer Container { get; } =
        new QdrantBuilder("qdrant/qdrant:v1.13.4").Build();

    public async Task InitializeAsync() => await Container.StartAsync();

    public async Task DisposeAsync() => await Container.DisposeAsync();
}

[CollectionDefinition("qdrant")]
public sealed class QdrantCollection : ICollectionFixture<QdrantFixture>;