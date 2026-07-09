using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Tests.TestDoubles;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

// Regression coverage for the entity-ingestion self-delete bug: the orchestrator upserted points
// tagged with the book's FileHash and then deleted every point carrying that same FileHash, wiping
// the batch it had just written. These tests use a fake store with real upsert/delete-by-filehash
// semantics (NSubstitute call-count mocks cannot catch a semantic self-delete).
public class EntityIngestionSelfDeleteTests
{
    [Fact]
    public async Task First_ingest_keeps_entities_in_store()
    {
        var store = new RecordingEntityVectorStore();
        var orchestrator = BuildOrchestrator(store, FixtureCanonicalDir());

        await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

        Assert.NotEmpty(store.Ids);
        Assert.Contains("test-book.class.fighter", store.Ids);
    }

    [Fact]
    public async Task Reingest_removes_stale_orphans_but_keeps_current_entities()
    {
        var store = new RecordingEntityVectorStore();
        // Seed a point from a prior extraction of the same book (same FileHash) whose entity is no
        // longer produced. It must be removed; current entities must remain.
        await store.UpsertAsync(
            [new EntityPoint(
                new EntityEnvelope(
                    Id: "test-book.class.removed-subclass",
                    Type: EntityType.Class,
                    Name: "Removed Subclass",
                    SourceBook: "Test Book",
                    Edition: "Edition2014",
                    Page: 1,
                    FirstAppearedIn: new FirstAppearance("Test Book", "Edition2014"),
                    RevisedIn: [],
                    SettingTags: [],
                    CanonicalText: "",
                    Fields: default),
                new float[1024],
                "deadbeef")],
            CancellationToken.None);

        var orchestrator = BuildOrchestrator(store, FixtureCanonicalDir());

        await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

        Assert.DoesNotContain("test-book.class.removed-subclass", store.Ids);
        Assert.Contains("test-book.class.fighter", store.Ids);
    }

    private static string FixtureCanonicalDir() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical");

    private static EntityIngestionOrchestrator BuildOrchestrator(IEntityVectorStore store, string canonicalDir)
    {
        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new IngestionRecord { Id = 1, DisplayName = "Test Book", FileHash = "deadbeef" });

        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IList<float[]>>(
                Enumerable.Range(0, ci.Arg<IList<string>>().Count).Select(_ => new float[1024]).ToList()));

        return new EntityIngestionOrchestrator(
            tracker,
            new CanonicalJsonLoader(),
            new EntityCanonicalTextDispatcher(),
            new EntityReferenceResolver(),
            embeddings,
            store,
            Options.Create(new EntityIngestionOptions { CanonicalDirectory = canonicalDir }),
            NullLogger<EntityIngestionOrchestrator>.Instance);
    }
}