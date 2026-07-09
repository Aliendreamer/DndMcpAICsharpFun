using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Ingestion;

using FluentAssertions;

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

        await store.Received(1).DeleteByFileHashExceptAsync(
            "deadbeef", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
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

        // Derive slug the same way the orchestrator does (prefers FivetoolsSourceKey)
        var slug = record.FivetoolsSourceKey is { } slugKey
            ? EntityIdSlug.For(slugKey, EntityType.Class, "x").Split('.')[0]
            : EntityIdSlug.For(record.DisplayName, EntityType.Class, "x").Split('.')[0];
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

    [Fact]
    public async Task IngestEntitiesAsync_MergesExisting5etoolsSrdFlag()
    {
        // Arrange
        const string displayName = "Test Book";
        const string fileHash = "deadbeef";

        var tracker = Substitute.For<IIngestionTracker>();
        var record = new IngestionRecord
        {
            Id = 1,
            DisplayName = displayName,
            FileHash = fileHash,
        };
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);

        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IList<float[]>>(
                Enumerable.Range(0, ci.Arg<IList<string>>().Count).Select(_ => new float[1024]).ToList()));

        // Build an existing envelope with srd: true for the first entity in the fixture
        var existingEnvelope = new EntityEnvelope(
            Id: "test-book.class.fighter",
            Type: EntityType.Class,
            Name: "Fighter",
            SourceBook: "Test Book",
            Edition: "Edition2014",
            Page: 70,
            FirstAppearedIn: new FirstAppearance("Test Book", "Edition2014"),
            RevisedIn: [],
            SettingTags: [],
            CanonicalText: "",
            Fields: default,
            DataSource: "5etools",
            Srd: true);

        IList<EntityPoint>? captured = null;
        var store = Substitute.For<IEntityVectorStore>();
        store.GetByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, EntityEnvelope>
            {
                ["test-book.class.fighter"] = existingEnvelope,
            });
        store.UpsertAsync(Arg.Any<IList<EntityPoint>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<IList<EntityPoint>>();
                return Task.CompletedTask;
            });

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

        // Act
        await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

        // Assert — GetByIdsAsync was called, and the fighter entity has srd:true from the merge
        await store.Received(1).GetByIdsAsync(
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());

        Assert.NotNull(captured);
        var fighter = captured!.FirstOrDefault(p => p.Envelope.Id == "test-book.class.fighter");
        Assert.NotNull(fighter);
        Assert.True(fighter!.Envelope.Srd, "Srd flag should be merged from the existing 5etools entity");
    }
}

public class EntityIngestionOrchestratorEnrichmentTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static string WriteCanonicalJson(string dir, string slug, string entityJson)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, slug + ".json");
        var wrapper = $$"""
            {
              "schemaVersion": "1",
              "book": { "sourceBook": "Test", "edition": "Edition2014",
                        "fileHash": "abc", "displayName": "Test" },
              "entities": [{{entityJson}}]
            }
            """;
        File.WriteAllText(path, wrapper);
        return path;
    }

    private static string WriteFivetoolsSpellsJson(string dir, string spellJson)
    {
        var spellsDir = Path.Combine(dir, "spells");
        Directory.CreateDirectory(spellsDir);
        var json = $$"""{ "spell": [{{spellJson}}] }""";
        File.WriteAllText(Path.Combine(spellsDir, "spells-phb.json"), json);
        return dir;
    }

    private static EntityIngestionOrchestrator BuildOrchestrator(
        IIngestionTracker tracker,
        IEmbeddingService embeddings,
        IEntityVectorStore store,
        string canonicalDir,
        string? fivetoolsDir = null)
    {
        return new EntityIngestionOrchestrator(
            tracker,
            new CanonicalJsonLoader(),
            new EntityCanonicalTextDispatcher(),
            new EntityReferenceResolver(),
            embeddings,
            store,
            Options.Create(new EntityIngestionOptions
            {
                CanonicalDirectory = canonicalDir,
                FivetoolsDirectory = fivetoolsDir ?? Path.Combine(Path.GetTempPath(), "absent-" + Guid.NewGuid()),
            }),
            NullLogger<EntityIngestionOrchestrator>.Instance);
    }

    private static IEmbeddingService MakeEmbeddings() =>
        Substitute.For<IEmbeddingService>().WithAny1024Vectors();

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enrichment_MatchingFivetoolsRecord_AppliesStructuredField()
    {
        // Arrange: canonical spell has noisy "cr" equivalent — we use "level" scalar —
        // and no "school"; 5etools has clean "school" = "V".
        var tmp = Path.Combine(Path.GetTempPath(), "enrich-test-" + Guid.NewGuid());
        try
        {
            // canonical: phb14.spell.fireball, no school field
            var canonical = """
                {
                  "id": "phb14.spell.fireball",
                  "type": "Spell", "name": "Fireball Noisy",
                  "sourceBook": "PHB", "edition": "Edition2014", "page": 241,
                  "firstAppearedIn": { "book": "PHB", "edition": "Edition2014" },
                  "revisedIn": [], "settingTags": [], "canonicalText": "",
                  "fields": { "level": 3, "entries": ["Our prose."] }
                }
                """;
            var canonicalDir = Path.Combine(tmp, "canonical");
            WriteCanonicalJson(canonicalDir, "phb14", canonical);

            // 5etools: matching id, has school, and has srd:true
            var ftSpell = """{ "name":"Fireball","source":"PHB","page":241,"level":3,"school":"V","srd":true }""";
            var ftDir = Path.Combine(tmp, "5etools");
            WriteFivetoolsSpellsJson(ftDir, ftSpell);

            var tracker = Substitute.For<IIngestionTracker>();
            tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new IngestionRecord
            {
                Id = 1,
                DisplayName = "PHB",
                FileHash = "abc",
                FivetoolsSourceKey = "PHB",
            });

            IList<EntityPoint>? captured = null;
            var store = Substitute.For<IEntityVectorStore>();
            store.UpsertAsync(Arg.Any<IList<EntityPoint>>(), Arg.Any<CancellationToken>())
                .Returns(ci => { captured = ci.Arg<IList<EntityPoint>>(); return Task.CompletedTask; });

            var orchestrator = BuildOrchestrator(tracker, MakeEmbeddings(), store, canonicalDir, ftDir);

            // Act
            await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

            // Assert: 5etools "school" filled in and "entries" (narrative) preserved from canonical
            Assert.NotNull(captured);
            var fireball = captured!.Single(p => p.Envelope.Id == "phb14.spell.fireball");
            fireball.Envelope.Fields.TryGetProperty("school", out var school).Should().BeTrue(
                "5etools school should be merged in");
            school.GetString().Should().Be("V");

            fireball.Envelope.Fields.GetProperty("entries")[0].GetString()
                .Should().Be("Our prose.", "narrative entries must stay from canonical");

            fireball.Envelope.Srd.Should().BeTrue("SRD flag comes from 5etools");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task Enrichment_NoMatch_EntityUnchanged()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "enrich-test-nomatch-" + Guid.NewGuid());
        try
        {
            var canonical = """
                {
                  "id": "phb14.spell.fireball",
                  "type": "Spell", "name": "Fireball",
                  "sourceBook": "PHB", "edition": "Edition2014", "page": 241,
                  "firstAppearedIn": { "book": "PHB", "edition": "Edition2014" },
                  "revisedIn": [], "settingTags": [], "canonicalText": "",
                  "fields": { "level": 3, "entries": ["Original prose."] }
                }
                """;
            var canonicalDir = Path.Combine(tmp, "canonical");
            WriteCanonicalJson(canonicalDir, "phb14", canonical);

            // 5etools dir exists but has a completely different spell → no match for fireball
            var ftSpell = """{ "name":"Magic Missile","source":"PHB","page":257,"level":1 }""";
            var ftDir = Path.Combine(tmp, "5etools");
            WriteFivetoolsSpellsJson(ftDir, ftSpell);

            var tracker = Substitute.For<IIngestionTracker>();
            tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new IngestionRecord
            {
                Id = 1,
                DisplayName = "PHB",
                FileHash = "abc",
                FivetoolsSourceKey = "PHB",
            });

            IList<EntityPoint>? captured = null;
            var store = Substitute.For<IEntityVectorStore>();
            store.UpsertAsync(Arg.Any<IList<EntityPoint>>(), Arg.Any<CancellationToken>())
                .Returns(ci => { captured = ci.Arg<IList<EntityPoint>>(); return Task.CompletedTask; });

            var orchestrator = BuildOrchestrator(tracker, MakeEmbeddings(), store, canonicalDir, ftDir);

            await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

            Assert.NotNull(captured);
            var fireball = captured!.Single(p => p.Envelope.Id == "phb14.spell.fireball");
            // No "school" field because there was no match
            fireball.Envelope.Fields.TryGetProperty("school", out _).Should().BeFalse();
            fireball.Envelope.Fields.GetProperty("entries")[0].GetString()
                .Should().Be("Original prose.");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task Enrichment_FivetoolsOnlyRecord_NotAddedToUpsert()
    {
        // 5etools has TWO spells; canonical has ONE → only 1 entity upserted.
        var tmp = Path.Combine(Path.GetTempPath(), "enrich-test-noadd-" + Guid.NewGuid());
        try
        {
            var canonical = """
                {
                  "id": "phb14.spell.fireball",
                  "type": "Spell", "name": "Fireball",
                  "sourceBook": "PHB", "edition": "Edition2014", "page": 241,
                  "firstAppearedIn": { "book": "PHB", "edition": "Edition2014" },
                  "revisedIn": [], "settingTags": [], "canonicalText": "",
                  "fields": { "level": 3 }
                }
                """;
            var canonicalDir = Path.Combine(tmp, "canonical");
            WriteCanonicalJson(canonicalDir, "phb14", canonical);

            // 5etools has fireball + magic-missile; only fireball is in canonical
            var ftSpells = """
                { "name":"Fireball","source":"PHB","page":241,"level":3,"school":"V","srd":true },
                { "name":"Magic Missile","source":"PHB","page":257,"level":1 }
                """;
            var ftDir = Path.Combine(tmp, "5etools");
            WriteFivetoolsSpellsJson(ftDir, ftSpells);

            var tracker = Substitute.For<IIngestionTracker>();
            tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new IngestionRecord
            {
                Id = 1,
                DisplayName = "PHB",
                FileHash = "abc",
                FivetoolsSourceKey = "PHB",
            });

            IList<EntityPoint>? captured = null;
            var store = Substitute.For<IEntityVectorStore>();
            store.UpsertAsync(Arg.Any<IList<EntityPoint>>(), Arg.Any<CancellationToken>())
                .Returns(ci => { captured = ci.Arg<IList<EntityPoint>>(); return Task.CompletedTask; });

            var orchestrator = BuildOrchestrator(tracker, MakeEmbeddings(), store, canonicalDir, ftDir);

            await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

            Assert.NotNull(captured);
            // Must be exactly 1 point (fireball) — magic missile must NOT be added.
            captured!.Count.Should().Be(1);
            captured[0].Envelope.Id.Should().Be("phb14.spell.fireball");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task Enrichment_AbsentFivetoolsDir_IngestsUnenriched()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "enrich-test-absent-" + Guid.NewGuid());
        try
        {
            var canonical = """
                {
                  "id": "phb14.spell.fireball",
                  "type": "Spell", "name": "Fireball",
                  "sourceBook": "PHB", "edition": "Edition2014", "page": 241,
                  "firstAppearedIn": { "book": "PHB", "edition": "Edition2014" },
                  "revisedIn": [], "settingTags": [], "canonicalText": "",
                  "fields": { "level": 3 }
                }
                """;
            var canonicalDir = Path.Combine(tmp, "canonical");
            WriteCanonicalJson(canonicalDir, "phb14", canonical);

            var tracker = Substitute.For<IIngestionTracker>();
            tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new IngestionRecord
            {
                Id = 1,
                DisplayName = "PHB",
                FileHash = "abc",
                FivetoolsSourceKey = "PHB",
            });

            IList<EntityPoint>? captured = null;
            var store = Substitute.For<IEntityVectorStore>();
            store.UpsertAsync(Arg.Any<IList<EntityPoint>>(), Arg.Any<CancellationToken>())
                .Returns(ci => { captured = ci.Arg<IList<EntityPoint>>(); return Task.CompletedTask; });

            // No fivetoolsDir passed → defaults to an absent path → graceful degradation
            var orchestrator = BuildOrchestrator(tracker, MakeEmbeddings(), store, canonicalDir);

            await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

            Assert.NotNull(captured);
            captured!.Count.Should().Be(1, "entity ingested without enrichment");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task CoverageResult_CorrectCounts_Reported()
    {
        // canonical: 2 spells; 5etools matches 1 → enriched=1, unmatched=1
        var tmp = Path.Combine(Path.GetTempPath(), "enrich-test-counts-" + Guid.NewGuid());
        try
        {
            var entities = """
                {
                  "id": "phb14.spell.fireball",
                  "type": "Spell", "name": "Fireball",
                  "sourceBook": "PHB", "edition": "Edition2014", "page": 241,
                  "firstAppearedIn": { "book": "PHB", "edition": "Edition2014" },
                  "revisedIn": [], "settingTags": [], "canonicalText": "",
                  "fields": { "level": 3 }
                },
                {
                  "id": "phb14.spell.light",
                  "type": "Spell", "name": "Light",
                  "sourceBook": "PHB", "edition": "Edition2014", "page": 255,
                  "firstAppearedIn": { "book": "PHB", "edition": "Edition2014" },
                  "revisedIn": [], "settingTags": [], "canonicalText": "",
                  "fields": { "level": 0 }
                }
                """;
            var canonicalDir = Path.Combine(tmp, "canonical");
            WriteCanonicalJson(canonicalDir, "phb14", entities);

            // 5etools only has fireball → Light is unmatched
            var ftSpell = """{ "name":"Fireball","source":"PHB","page":241,"level":3,"school":"V" }""";
            var ftDir = Path.Combine(tmp, "5etools");
            WriteFivetoolsSpellsJson(ftDir, ftSpell);

            var tracker = Substitute.For<IIngestionTracker>();
            tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new IngestionRecord
            {
                Id = 1,
                DisplayName = "PHB",
                FileHash = "abc",
                FivetoolsSourceKey = "PHB",
            });

            var store = Substitute.For<IEntityVectorStore>();

            var orchestrator = BuildOrchestrator(tracker, MakeEmbeddings(), store, canonicalDir, ftDir);

            var result = await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

            result.TotalEntities.Should().Be(2);
            result.MatchedFivetools.Should().Be(1);
            result.Unmatched.Should().Be(1);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task UngroundedDisposition_Entity_ExcludedFromUpsert()
    {
        // entity-grounding-cascade Fix I-1(b): a canonical entity marked Ungrounded (a
        // judge-confirmed fabrication) must never be (re-)added to dnd_entities, even on a full
        // re-ingest — NeedsReview entities are unaffected and stay indexed as before.
        var tmp = Path.Combine(Path.GetTempPath(), "ingest-test-ungrounded-" + Guid.NewGuid());
        try
        {
            var entities = """
                {
                  "id": "phb14.spell.fireball",
                  "type": "Spell", "name": "Fireball",
                  "sourceBook": "PHB", "edition": "Edition2014", "page": 241,
                  "firstAppearedIn": { "book": "PHB", "edition": "Edition2014" },
                  "revisedIn": [], "settingTags": [], "canonicalText": "",
                  "fields": { "level": 3 }
                },
                {
                  "id": "phb14.spell.needs-review-spell",
                  "type": "Spell", "name": "Needs Review Spell",
                  "sourceBook": "PHB", "edition": "Edition2014", "page": 500,
                  "firstAppearedIn": { "book": "PHB", "edition": "Edition2014" },
                  "revisedIn": [], "settingTags": [], "canonicalText": "",
                  "fields": { "level": 5 }, "disposition": "NeedsReview"
                },
                {
                  "id": "phb14.spell.fabricated-bolt",
                  "type": "Spell", "name": "Fabricated Bolt",
                  "sourceBook": "PHB", "edition": "Edition2014", "page": 999,
                  "firstAppearedIn": { "book": "PHB", "edition": "Edition2014" },
                  "revisedIn": [], "settingTags": [], "canonicalText": "",
                  "fields": { "level": 9 }, "disposition": "Ungrounded"
                }
                """;
            var canonicalDir = Path.Combine(tmp, "canonical");
            WriteCanonicalJson(canonicalDir, "phb14", entities);

            var tracker = Substitute.For<IIngestionTracker>();
            tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new IngestionRecord
            {
                Id = 1,
                DisplayName = "PHB",
                FileHash = "abc",
                FivetoolsSourceKey = "PHB",
            });

            IList<EntityPoint>? captured = null;
            var store = Substitute.For<IEntityVectorStore>();
            store.UpsertAsync(Arg.Any<IList<EntityPoint>>(), Arg.Any<CancellationToken>())
                .Returns(ci => { captured = ci.Arg<IList<EntityPoint>>(); return Task.CompletedTask; });

            var orchestrator = BuildOrchestrator(tracker, MakeEmbeddings(), store, canonicalDir);

            await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

            Assert.NotNull(captured);
            captured!.Select(p => p.Envelope.Id).Should().BeEquivalentTo(
                ["phb14.spell.fireball", "phb14.spell.needs-review-spell"],
                "the Ungrounded entity must be excluded while Accepted/NeedsReview entities stay indexed");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}

/// <summary>Test helper extension to avoid boilerplate in substitute setup.</summary>
file static class EmbeddingsSubstituteExtensions
{
    internal static IEmbeddingService WithAny1024Vectors(this IEmbeddingService svc)
    {
        svc.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IList<float[]>>(
                Enumerable.Range(0, ci.Arg<IList<string>>().Count)
                          .Select(_ => new float[1024]).ToList()));
        return svc;
    }
}