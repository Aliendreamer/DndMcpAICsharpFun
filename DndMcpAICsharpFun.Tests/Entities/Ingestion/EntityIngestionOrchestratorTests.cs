using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public class EntityIngestionOrchestratorTests
{
    [Fact]
    public async Task Ingests_twelve_entities_from_fixture_and_calls_upsert_once()
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
            Arg.Is<IList<EntityPoint>>(p => p.Count == 22),
            Arg.Any<CancellationToken>());
        await tracker.Received(1).MarkEntitiesIngestedAsync(1, 22, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ingests_entities_with_llm_data_source_stamp()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var record = new IngestionRecord { Id = 1, DisplayName = "Test Book", FileHash = "deadbeef" };
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);
        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IList<float[]>>(
                Enumerable.Range(0, ci.Arg<IList<string>>().Count).Select(_ => new float[1024]).ToList()));
        var store = Substitute.For<IEntityVectorStore>();
        var canonicalDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical");
        var orchestrator = new EntityIngestionOrchestrator(
            tracker, new CanonicalJsonLoader(), new EntityCanonicalTextDispatcher(),
            new EntityReferenceResolver(), embeddings, store,
            Options.Create(new EntityIngestionOptions { CanonicalDirectory = canonicalDir }),
            NullLogger<EntityIngestionOrchestrator>.Instance);

        await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

        await store.Received(1).UpsertAsync(
            Arg.Is<IList<EntityPoint>>(pts => pts.All(p => p.Envelope.DataSource == "llm")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestEntitiesAsync_BookWithSourceKey_NormalisesSourceBookInQdrantPoint()
    {
        // Arrange
        const string fivetoolsKey = "PHB";
        const string displaySourceBook = "Player's Handbook (2014)";

        var tracker = Substitute.For<IIngestionTracker>();
        var record = new IngestionRecord
        {
            Id = 99,
            DisplayName = displaySourceBook,
            FileHash = "aabbccdd",
            FivetoolsSourceKey = fivetoolsKey,
        };
        tracker.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns(record);

        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IList<float[]>>(
                Enumerable.Range(0, ci.Arg<IList<string>>().Count).Select(_ => new float[1024]).ToList()));

        IList<EntityPoint>? captured = null;
        var store = Substitute.For<IEntityVectorStore>();
        store.UpsertAsync(Arg.Any<IList<EntityPoint>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<IList<EntityPoint>>();
                return Task.CompletedTask;
            });

        // Derive slug the same way the orchestrator does
        var slug = EntityIdSlug
            .For(record.DisplayName, EntityType.Class, "x")
            .Split('.')[0];
        var tempDir = Path.GetTempPath();
        var canonicalPath = Path.Combine(tempDir, slug + ".json");

        var canonicalJson = """
            {
              "schemaVersion": "1",
              "book": { "sourceBook": "Player's Handbook (2014)", "edition": "Edition2014", "fileHash": "abc", "displayName": "Player's Handbook" },
              "entities": [{
                "id": "phb.class.fighter", "type": "Class", "name": "Fighter",
                "sourceBook": "Player's Handbook (2014)", "edition": "Edition2014",
                "page": 70,
                "firstAppearedIn": { "book": "Player's Handbook (2014)", "edition": "Edition2014", "page": 70 },
                "revisedIn": [], "settingTags": [], "canonicalText": "", "fields": {}
              }]
            }
            """;
        await File.WriteAllTextAsync(canonicalPath, canonicalJson);

        try
        {
            var orchestrator = new EntityIngestionOrchestrator(
                tracker,
                new CanonicalJsonLoader(),
                new EntityCanonicalTextDispatcher(),
                new EntityReferenceResolver(),
                embeddings,
                store,
                Options.Create(new EntityIngestionOptions { CanonicalDirectory = tempDir }),
                NullLogger<EntityIngestionOrchestrator>.Instance);

            // Act
            await orchestrator.IngestEntitiesAsync(99, CancellationToken.None);

            // Assert
            Assert.NotNull(captured);
            Assert.Single(captured!);
            Assert.Equal(fivetoolsKey, captured![0].Envelope.SourceBook);
        }
        finally
        {
            if (File.Exists(canonicalPath)) File.Delete(canonicalPath);
        }
    }
}
