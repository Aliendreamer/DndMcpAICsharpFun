using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

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
        var store = new FakeEntityVectorStore();
        var orchestrator = BuildOrchestrator(store, FixtureCanonicalDir());

        await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

        Assert.NotEmpty(store.Ids);
        Assert.Contains("test-book.class.fighter", store.Ids);
    }

    [Fact]
    public async Task Reingest_removes_stale_orphans_but_keeps_current_entities()
    {
        var store = new FakeEntityVectorStore();
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

// Fake store with real upsert + delete-by-filehash semantics, mirroring Qdrant: points are keyed by
// entity id (upsert overwrites), and a delete removes every stored point whose FileHash matches.
file sealed class FakeEntityVectorStore : IEntityVectorStore
{
    private readonly Dictionary<string, (EntityEnvelope Env, string FileHash)> _store = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Ids => _store.Keys;

    public Task UpsertAsync(IList<EntityPoint> points, CancellationToken ct = default)
    {
        foreach (var p in points)
            _store[p.Envelope.Id] = (p.Envelope, p.FileHash);
        return Task.CompletedTask;
    }

    public Task DeleteByFileHashAsync(string fileHash, CancellationToken ct = default)
    {
        foreach (var id in _store.Where(kv => kv.Value.FileHash == fileHash).Select(kv => kv.Key).ToList())
            _store.Remove(id);
        return Task.CompletedTask;
    }

    public Task DeleteByFileHashExceptAsync(
        string fileHash, IReadOnlyCollection<string> keepIds, CancellationToken ct = default)
    {
        var keep = keepIds.ToHashSet(StringComparer.Ordinal);
        foreach (var id in _store
                     .Where(kv => kv.Value.FileHash == fileHash && !keep.Contains(kv.Key))
                     .Select(kv => kv.Key).ToList())
            _store.Remove(id);
        return Task.CompletedTask;
    }

    public Task<EntityEnvelope?> GetByIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(id, out var v) ? v.Env : null);

    public Task<IReadOnlyDictionary<string, EntityEnvelope>> GetByIdsAsync(
        IReadOnlyList<string> entityIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, EntityEnvelope>>(
            _store.Where(kv => entityIds.Contains(kv.Key))
                  .ToDictionary(kv => kv.Key, kv => kv.Value.Env, StringComparer.Ordinal));

    public Task<IList<EntitySearchHit>> SearchAsync(
        float[] queryVector, EntityFilters filters, int topK, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, string>> GetDataSourcesAsync(
        IReadOnlyList<string> entityIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal));
}
