using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Ingestion;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

/// <summary>5etools-driven writers must never touch programmatic data; reindex rules for programmatic entities.</summary>
public sealed class ProgrammaticGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prog-guard-" + Guid.NewGuid().ToString("N"));

    public ProgrammaticGuardTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private static string Entity(string id, string type, string name, string dataSource, string disposition = "Accepted") => $$"""
        { "id": "{{id}}", "type": "{{type}}", "name": "{{name}}", "sourceBook": "PHB",
          "edition": "Edition2014", "page": 1,
          "firstAppearedIn": { "book": "PHB", "edition": "Edition2014", "page": 1 },
          "revisedIn": [], "settingTags": [], "canonicalText": "",
          "fields": { "entries": [] }, "dataSource": "{{dataSource}}", "disposition": "{{disposition}}" }
        """;

    private string WriteCanonical(string slug, params string[] entities)
    {
        var path = Path.Combine(_root, slug + ".json");
        File.WriteAllText(path, $$"""
            { "schemaVersion": "2",
              "book": { "sourceBook": "PHB", "edition": "Edition2014", "fileHash": "", "displayName": "PHB" },
              "entities": [ {{string.Join(",", entities)}} ] }
            """);
        return path;
    }

    private static IngestionRecord Record(string? key = "PHB") => new()
    {
        Id = 1, FilePath = "/x.pdf", FileName = "x.pdf", FileHash = "h", Version = "5e",
        DisplayName = "Player's Handbook 2014", Status = IngestionStatus.JsonIngested, FivetoolsSourceKey = key,
    };

    private sealed class OneClassProvider : IFivetoolsBackfillProvider
    {
        public EntityType Type => EntityType.Class;
        public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
        {
            using var doc = JsonDocument.Parse("""[ { "name": "Fighter", "source": "PHB" } ]""");
            foreach (var e in doc.RootElement.EnumerateArray()) yield return e.Clone();
        }
        public EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element) =>
            new(EntityIdSlug.For(sourceKey, Type, name), Type, name, sourceKey, edition, null,
                new FirstAppearance(sourceKey, edition, null), [], [], "", JsonDocument.Parse("{}").RootElement.Clone(),
                DataSource: "5etools-backfill");
    }

    [Fact]
    public async Task Backfill_skips_programmatic_book()
    {
        File.WriteAllText(Path.Combine(_root, "books.json"), """
            { "book": [ { "id": "PHB", "name": "PHB", "group": "core", "published": "2014-08-19" } ] }
            """);
        WriteCanonical("phb14", Entity("phb14.class.barbarian", "Class", "Barbarian", "programmatic"));
        var svc = new EntityBackfillService(new OneClassProvider(),
            new BookSourceRegistry(Path.Combine(_root, "books.json")), new CanonicalJsonLoader(), _root, _root);

        var result = await svc.ComputeAsync(Record(), CancellationToken.None);

        result.SkippedProgrammatic.Should().BeTrue();
        result.ToAppend.Should().BeEmpty();
        result.ExtraUnknown.Should().BeEmpty();

        var flag = await svc.FlagUnknownAsync(Record(), new CanonicalJsonWriter(), CancellationToken.None);
        flag.Flagged.Should().BeEmpty();
    }

    [Fact]
    public async Task Fivetools_import_never_overwrites_programmatic_points()
    {
        var store = Substitute.For<IEntityVectorStore>();
        var embeddings = Substitute.For<IEmbeddingService>();
        store.GetDataSourcesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string> { ["phb.class.fighter"] = "programmatic" });
        embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IList<float[]>>(ci.Arg<IList<string>>().Select(_ => new float[1024]).ToList()));
        static EntityEnvelope Env(string id, string name) => new(id, EntityType.Class, name, "PHB", "Edition2014", null,
            new FirstAppearance("PHB", "Edition2014"), [], [], "", default, "5etools");
        var svc = new FivetoolsIngestionService(store, embeddings, new EntityCanonicalTextDispatcher(),
            Options.Create(new EntityIngestionOptions { CanonicalDirectory = _root }),
            NullLogger<FivetoolsIngestionService>.Instance);

        await svc.IngestEnvelopesAsync([Env("phb.class.fighter", "Fighter"), Env("phb.class.wizard", "Wizard")], CancellationToken.None);

        await store.Received(1).UpsertAsync(
            Arg.Is<IList<EntityPoint>>(p => p.Count == 1 && p[0].Envelope.Name == "Wizard"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fivetools_import_drops_every_envelope_of_a_programmatic_book()
    {
        // Flagged programmatic entities are deliberately NOT in Qdrant, so the id guard alone cannot protect them:
        // the import must skip the whole book once its canonical file is programmatic.
        WriteCanonical("phb14", Entity("phb14.spell.fireball", "Spell", "Fireball", "programmatic", "NeedsReview"));
        var store = new DndMcpAICsharpFun.Tests.TestDoubles.RecordingEntityVectorStore();
        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IList<float[]>>(ci.Arg<IList<string>>().Select(_ => new float[4]).ToList()));
        static EntityEnvelope Env(string id, string name, string book) => new(id, EntityType.Spell, name, book, "Edition2014",
            null, new FirstAppearance(book, "Edition2014"), [], [], "", default, "5etools");
        var svc = new FivetoolsIngestionService(store, embeddings, new EntityCanonicalTextDispatcher(),
            Options.Create(new EntityIngestionOptions { CanonicalDirectory = _root }),
            NullLogger<FivetoolsIngestionService>.Instance);

        var result = await svc.IngestEnvelopesAsync([
            Env("phb14.spell.fireball", "Fireball", "PHB"),  // flagged programmatic: not in the store at all
            Env("phb14.spell.shield", "Shield", "PHB"),
            Env("xge14.spell.toll-the-dead", "Toll the Dead", "XGE"),
        ], CancellationToken.None);

        store.Ids.Should().BeEquivalentTo(["xge14.spell.toll-the-dead"]);
        result.Should().BeEquivalentTo(new
        {
            Mapped = 3, Upserted = 1, SkippedProgrammaticBook = 2, SkippedExisting = 0,
            ProgrammaticBooks = new[] { "PHB" },
        });
    }

    [Fact]
    public async Task Purge_removes_only_the_books_5etools_import_points()
    {
        var store = new DndMcpAICsharpFun.Tests.TestDoubles.RecordingEntityVectorStore();
        static EntityPoint Pt(string id, string hash) => new(new EntityEnvelope(id, EntityType.Spell, id, "PHB",
            "Edition2014", null, new FirstAppearance("PHB", "Edition2014"), [], [], "", default, "5etools"), new float[4], hash);
        await store.UpsertAsync([Pt("a", "5etools:PHB"), Pt("b", "5etools:MM"), Pt("c", "phb-file-hash")]);

        await FivetoolsImportedPoints.PurgeAsync(store, "phb", CancellationToken.None);

        store.Ids.Should().BeEquivalentTo(["b", "c"]);
    }

    [Fact]
    public async Task FieldFill_skips_programmatic_entity()
    {
        Directory.CreateDirectory(Path.Combine(_root, "class"));
        File.WriteAllText(Path.Combine(_root, "class", "class-barbarian.json"), """
            { "class": [ { "name": "Barbarian", "source": "PHB", "hd": { "number": 1, "faces": 12 } } ] }
            """);
        var path = WriteCanonical("phb14", Entity("phb14.class.barbarian", "Class", "Barbarian", "programmatic"));
        var svc = new EntityFieldFillService(new CanonicalJsonLoader(), new CanonicalJsonWriter(), _root, _root);

        var result = await svc.FillAsync(Record(), CancellationToken.None);

        result.EntitiesTouched.Should().Be(0);
        var file = await new CanonicalJsonLoader().LoadAsync(path, CancellationToken.None);
        file.Entities.Single().Fields.TryGetProperty("hd", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectTables_skips_programmatic_book()
    {
        var path = WriteCanonical("phb14", Entity("phb14.class.barbarian", "Class", "Barbarian", "programmatic"));
        var before = File.ReadAllText(path);

        var res = await ProjectTablesRunner.RunOneAsync(path, _root, new CanonicalJsonLoader(), new CanonicalJsonWriter(), default);

        res.Skipped.Should().BeTrue();
        File.ReadAllText(path).Should().Be(before);
    }

    private (EntityIngestionOrchestrator Orch, IEntityVectorStore Store) ReindexSetup(
        string disposition, EntityEnvelope? existing)
    {
        var record = new IngestionRecord { Id = 5, DisplayName = $"Reindex Book {Guid.NewGuid():N}", FileHash = "h" };
        WriteCanonical(EntityIdSlug.BookSlug(record),
            Entity("rb.feat.alert", "Feat", "Alert", "programmatic", disposition));
        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(record);
        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IList<float[]>>(ci.Arg<IList<string>>().Select(_ => new float[1024]).ToList()));
        var store = Substitute.For<IEntityVectorStore>();
        store.GetByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(existing is null
                ? new Dictionary<string, EntityEnvelope>()
                : new Dictionary<string, EntityEnvelope> { ["rb.feat.alert"] = existing });
        var orch = new EntityIngestionOrchestrator(tracker, new CanonicalJsonLoader(), new EntityCanonicalTextDispatcher(),
            new EntityReferenceResolver(), embeddings, store,
            Options.Create(new EntityIngestionOptions { CanonicalDirectory = _root, FivetoolsDirectory = Path.Combine(_root, "none") }),
            NullLogger<EntityIngestionOrchestrator>.Instance);
        return (orch, store);
    }

    [Fact]
    public async Task Reindex_non_accepted_programmatic_deletes_point()
    {
        var (orch, store) = ReindexSetup("NeedsReview", null);

        await orch.ReindexEntityAsync(5, "rb.feat.alert", CancellationToken.None);

        await store.Received(1).DeleteByIdsAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.Single() == "rb.feat.alert"), Arg.Any<CancellationToken>());
        await store.DidNotReceive().UpsertAsync(Arg.Any<IList<EntityPoint>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reindex_accepted_programmatic_upserts_without_merge()
    {
        var existing = new EntityEnvelope("rb.feat.alert", EntityType.Feat, "Alert", "PHB", "Edition2014", 1,
            new FirstAppearance("PHB", "Edition2014"), [], [], "", default, DataSource: "5etools", Srd: true);
        var (orch, store) = ReindexSetup("Accepted", existing);
        IList<EntityPoint>? captured = null;
        store.UpsertAsync(Arg.Any<IList<EntityPoint>>(), Arg.Any<CancellationToken>())
            .Returns(ci => { captured = ci.Arg<IList<EntityPoint>>(); return Task.CompletedTask; });

        await orch.ReindexEntityAsync(5, "rb.feat.alert", CancellationToken.None);

        captured.Should().ContainSingle().Which.Envelope.Srd.Should().BeFalse();
    }
}
