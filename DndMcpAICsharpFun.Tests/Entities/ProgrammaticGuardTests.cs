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
            NullLogger<FivetoolsIngestionService>.Instance);

        await svc.IngestEnvelopesAsync([Env("phb.class.fighter", "Fighter"), Env("phb.class.wizard", "Wizard")], CancellationToken.None);

        await store.Received(1).UpsertAsync(
            Arg.Is<IList<EntityPoint>>(p => p.Count == 1 && p[0].Envelope.Name == "Wizard"), Arg.Any<CancellationToken>());
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
