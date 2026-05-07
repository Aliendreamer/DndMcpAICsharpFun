using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public class EntityIngestionOrchestratorTests
{
    [Fact]
    public async Task Ingests_five_entities_from_fixture_and_calls_upsert_once()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var record = new IngestionRecord
        {
            Id = 1,
            DisplayName = "Test Book",
            FileHash = "deadbeef",
        };
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);

        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var texts = ci.Arg<IList<string>>();
                return Task.FromResult<IList<float[]>>(
                    texts.Select(_ => new float[1024]).ToList());
            });

        var store = Substitute.For<IEntityVectorStore>();

        var canonicalDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical");

        var orchestrator = new EntityIngestionOrchestrator(
            tracker,
            new CanonicalJsonLoader(),
            new EntityCanonicalTextDispatcher(),
            new EntityReferenceResolver(),
            embeddings,
            store,
            Options.Create(new EntityIngestionOptions { CanonicalDirectory = canonicalDir }),
            NullLogger<EntityIngestionOrchestrator>.Instance);

        await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

        await store.Received(1).DeleteByFileHashAsync("deadbeef", Arg.Any<CancellationToken>());
        await store.Received(1).UpsertAsync(
            Arg.Is<IList<EntityPoint>>(p => p.Count == 5),
            Arg.Any<CancellationToken>());
        await tracker.Received(1).MarkEntitiesIngestedAsync(1, 5, Arg.Any<CancellationToken>());
    }
}
