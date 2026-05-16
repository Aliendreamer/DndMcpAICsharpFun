# Entity Data Unification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a single authoritative Qdrant point per D&D entity with unified IDs across both ingestion pipelines and best-of-both-sources field data.

**Architecture:** `EntityIdSlug.BookOverrides` is extended with source key aliases so both pipelines produce identical IDs. `EntityMerger` encapsulates per-field merge logic. `EntityIngestionOrchestrator` batch-fetches existing data and merges before upserting. `CanonicalTypeFixerService` one-shot corrects entity types in canonical JSONs using 5etools as type reference. Deterministic UUIDs ensure upsert overwrites the correct Qdrant point.

**Tech Stack:** .NET 10, C#, NSubstitute (tests), xUnit, Qdrant.Client, System.Text.Json

**Current broken state:** A partial implementation of deterministic UUIDs is already in `QdrantEntityVectorStore.ToPoint` — it calls `Guid.CreateVersion5(s_entityNs, ...)` but the `s_entityNs` static field was never added. Task 1 fixes this compile error. `EntityIngestionOrchestrator` also has a `RewriteBookPrefix` method and runtime ID rewriting that must be reverted in Task 4 (the design uses source-level ID fixing instead).

---

## Task 1: Fix EntityIdSlug.BookOverrides + Deterministic UUID static field

**Files:**

- Modify: `Domain/Entities/EntityIdSlug.cs`
- Modify: `Features/VectorStore/Entities/QdrantEntityVectorStore.cs`
- Test: `DndMcpAICsharpFun.Tests/Domain/EntityIdSlugTests.cs`

- [ ] **Step 1: Write failing tests for source key aliases**

```csharp
// DndMcpAICsharpFun.Tests/Domain/EntityIdSlugTests.cs
using DndMcpAICsharpFun.Domain.Entities;
using FluentAssertions;

public class EntityIdSlugTests
{
    [Theory]
    [InlineData("TCE",  "tce")]
    [InlineData("PHB",  "phb14")]
    [InlineData("DMG",  "dmg14")]
    [InlineData("XPHB", "phb24")]
    [InlineData("XDMG", "dmg24")]
    [InlineData("MM",   "mm14")]
    [InlineData("MM25", "mm24")]
    [InlineData("XGTE", "xgte")]
    [InlineData("MPMM", "mpmm")]
    [InlineData("VGM",  "vgm")]
    [InlineData("ERLW", "erlw")]
    public void Source_key_produces_expected_book_prefix(string sourceKey, string expectedPrefix)
    {
        var id = EntityIdSlug.For(sourceKey, EntityType.Class, "Fighter");
        id.Should().StartWith(expectedPrefix + ".");
    }

    [Fact]
    public void TCE_source_key_and_display_name_produce_same_prefix()
    {
        var fromKey  = EntityIdSlug.For("TCE",                            EntityType.Subclass, "Circle of Spores");
        var fromName = EntityIdSlug.For("Tasha's Cauldron of Everything", EntityType.Subclass, "Circle of Spores");
        fromKey.Should().Be(fromName);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "EntityIdSlugTests" -v minimal
```

Expected: FAIL — `tce` prefix not yet produced for `"TCE"`.

- [ ] **Step 3: Update BookOverrides in EntityIdSlug.cs**

Replace the current `BookOverrides` dictionary with:

```csharp
private static readonly Dictionary<string, string> BookOverrides = new(StringComparer.OrdinalIgnoreCase)
{
    // Display name → slug
    ["Player's Handbook 2014"]                        = "phb14",
    ["Player's Handbook 2024"]                        = "phb24",
    ["Monster Manual 2014"]                           = "mm14",
    ["Monster Manual 2024"]                           = "mm24",
    ["Dungeon Master's Guide 2014"]                   = "dmg14",
    ["Dungeon Master's Guide 2024"]                   = "dmg24",
    ["Dungeon Master's Guide"]                        = "dmg14",
    ["Tasha's Cauldron of Everything"]                = "tce",
    ["Xanathar's Guide to Everything"]                = "xgte",
    ["Volo's Guide to Monsters"]                      = "vgm",
    ["Mordenkainen Presents: Monsters of the Multiverse"] = "mpmm",
    ["Eberron: Rising from the Last War"]             = "erlw",
    // Source key → slug (aligns 5etools pipeline with canonical pipeline)
    ["PHB"]  = "phb14",
    ["XPHB"] = "phb24",
    ["DMG"]  = "dmg14",
    ["XDMG"] = "dmg24",
    ["MM"]   = "mm14",
    ["MM25"] = "mm24",
    ["TCE"]  = "tce",
    ["XGTE"] = "xgte",
    ["MPMM"] = "mpmm",
    ["VGM"]  = "vgm",
    ["ERLW"] = "erlw",
};
```

- [ ] **Step 4: Add missing static field to QdrantEntityVectorStore**

In `Features/VectorStore/Entities/QdrantEntityVectorStore.cs`, add this field directly after the `private readonly string _collection` field:

```csharp
private static readonly Guid s_entityNs = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
```

- [ ] **Step 5: Run tests and build**

```bash
dotnet build && dotnet test --filter "EntityIdSlugTests" -v minimal
```

Expected: BUILD SUCCESS, all EntityIdSlugTests PASS.

- [ ] **Step 6: Commit**

```bash
git add Domain/Entities/EntityIdSlug.cs Features/VectorStore/Entities/QdrantEntityVectorStore.cs DndMcpAICsharpFun.Tests/Domain/EntityIdSlugTests.cs
git commit -m "feat(entity-ids): unify book prefix slugs — add source key aliases to BookOverrides, fix missing s_entityNs field"
```

---

## Task 2: GetByIdsAsync on IEntityVectorStore

**Files:**

- Modify: `Features/VectorStore/Entities/IEntityVectorStore.cs`
- Modify: `Features/VectorStore/Entities/QdrantEntityVectorStore.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/VectorStore/QdrantEntityVectorStoreTests.cs` (if exists, else create)

- [ ] **Step 1: Add method to interface**

In `IEntityVectorStore.cs`, add:

```csharp
Task<IReadOnlyDictionary<string, EntityEnvelope>> GetByIdsAsync(
    IList<string> entityIds, CancellationToken ct = default);
```

- [ ] **Step 2: Implement in QdrantEntityVectorStore**

Add after the existing `GetByIdAsync` method:

```csharp
public async Task<IReadOnlyDictionary<string, EntityEnvelope>> GetByIdsAsync(
    IList<string> entityIds, CancellationToken ct = default)
{
    if (entityIds.Count == 0) return new Dictionary<string, EntityEnvelope>();

    var filter = new Filter();
    foreach (var id in entityIds)
        filter.Should.Add(KW(EntityPayloadFields.Id, id));

    const uint PageSize = 1000;
    var result = new Dictionary<string, EntityEnvelope>(entityIds.Count);
    PointId? offset = null;
    do
    {
        var page = await client.ScrollAsync(
            _collection, filter,
            offset: offset,
            limit: PageSize,
            payloadSelector: true,
            cancellationToken: ct);
        foreach (var p in page.Result.Where(p => p.Payload.ContainsKey(EntityPayloadFields.Id)))
        {
            var envelope = ToEnvelope(p.Payload);
            result[envelope.Id] = envelope;
        }
        offset = page.NextPageOffset;
    } while (offset is not null);

    return result;
}
```

- [ ] **Step 3: Write unit tests**

```csharp
// In orchestrator tests, verify GetByIdsAsync is called:
// DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs
// (Add after existing tests)

[Fact]
public async Task IngestEntitiesAsync_CallsGetByIdsAsync_ForMerge()
{
    var tracker = Substitute.For<IIngestionTracker>();
    var record = new IngestionRecord { Id = 1, DisplayName = "Test Book", FileHash = "deadbeef" };
    tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);

    var embeddings = Substitute.For<IEmbeddingService>();
    embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
        .Returns(ci => Task.FromResult<IList<float[]>>(
            Enumerable.Range(0, ci.Arg<IList<string>>().Count).Select(_ => new float[1024]).ToList()));

    var store = Substitute.For<IEntityVectorStore>();
    store.GetByIdsAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
        .Returns(new Dictionary<string, EntityEnvelope>());

    var canonicalDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical");
    var orchestrator = new EntityIngestionOrchestrator(
        tracker, new CanonicalJsonLoader(), new EntityCanonicalTextDispatcher(),
        new EntityReferenceResolver(), embeddings, store,
        Options.Create(new EntityIngestionOptions { CanonicalDirectory = canonicalDir }),
        NullLogger<EntityIngestionOrchestrator>.Instance);

    await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

    await store.Received(1).GetByIdsAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>());
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "EntityIngestionOrchestrator" -v minimal
```

Expected: New test FAILS (GetByIdsAsync not yet called from orchestrator — wired in Task 4).

- [ ] **Step 5: Commit interface + implementation (test will pass after Task 4)**

```bash
git add Features/VectorStore/Entities/IEntityVectorStore.cs Features/VectorStore/Entities/QdrantEntityVectorStore.cs DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs
git commit -m "feat(vector-store): add GetByIdsAsync batch fetch for pre-merge lookup"
```

---

## Task 3: EntityMerger

**Files:**

- Create: `Features/Ingestion/Entities/EntityMerger.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityMergerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityMergerTests.cs
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using FluentAssertions;
using System.Text.Json;

public class EntityMergerTests
{
    private static EntityEnvelope MakeEnvelope(
        string id = "tce.class.foo",
        EntityType type = EntityType.Class,
        string fields = "{}",
        string canonicalText = "",
        bool srd = false,
        bool srd52 = false,
        bool basicRules2024 = false,
        IReadOnlyList<string>? keywords = null,
        int? page = null,
        string dataSource = "llm") =>
        new(
            Id: id, Type: type, Name: "Foo", SourceBook: "TCE", Edition: "Edition2014",
            Page: page,
            FirstAppearedIn: new FirstAppearance("TCE", "Edition2014", page),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: canonicalText,
            Fields: JsonDocument.Parse(fields).RootElement.Clone(),
            DataSource: dataSource,
            Srd: srd, Srd52: srd52, BasicRules2024: basicRules2024,
            Keywords: keywords ?? Array.Empty<string>());

    [Fact]
    public void Canonical_fields_always_win()
    {
        var canonical = MakeEnvelope(fields: """{"key":"canonical"}""", canonicalText: "canonical text");
        var existing  = MakeEnvelope(fields: """{"key":"5etools"}""",  canonicalText: "old");
        var merged = EntityMerger.Merge(canonical, existing);
        merged.Fields.GetProperty("key").GetString().Should().Be("canonical");
        merged.CanonicalText.Should().Be("canonical text");
    }

    [Fact]
    public void Fivetools_srd_flags_always_win()
    {
        var canonical = MakeEnvelope(srd: false, srd52: false, basicRules2024: false);
        var existing  = MakeEnvelope(srd: true,  srd52: true,  basicRules2024: true,  dataSource: "5etools");
        var merged = EntityMerger.Merge(canonical, existing);
        merged.Srd.Should().BeTrue();
        merged.Srd52.Should().BeTrue();
        merged.BasicRules2024.Should().BeTrue();
    }

    [Fact]
    public void Fivetools_type_wins_when_canonical_type_is_Class()
    {
        var canonical = MakeEnvelope(type: EntityType.Class);
        var existing  = MakeEnvelope(type: EntityType.Subclass, dataSource: "5etools");
        var merged = EntityMerger.Merge(canonical, existing);
        merged.Type.Should().Be(EntityType.Subclass);
    }

    [Fact]
    public void Canonical_type_wins_when_not_Class()
    {
        var canonical = MakeEnvelope(type: EntityType.Spell);
        var existing  = MakeEnvelope(type: EntityType.Subclass, dataSource: "5etools");
        var merged = EntityMerger.Merge(canonical, existing);
        merged.Type.Should().Be(EntityType.Spell);
    }

    [Fact]
    public void Keywords_longest_list_wins()
    {
        var canonical = MakeEnvelope(keywords: ["a", "b", "c"]);
        var existing  = MakeEnvelope(keywords: ["a", "b"],       dataSource: "5etools");
        EntityMerger.Merge(canonical, existing).Keywords.Should().HaveCount(3);

        var canonical2 = MakeEnvelope(keywords: ["a"]);
        var existing2  = MakeEnvelope(keywords: ["a", "b", "c"], dataSource: "5etools");
        EntityMerger.Merge(canonical2, existing2).Keywords.Should().HaveCount(3);
    }

    [Fact]
    public void Page_existing_wins_if_set()
    {
        var canonical = MakeEnvelope(page: 42);
        var existing  = MakeEnvelope(page: 10, dataSource: "5etools");
        EntityMerger.Merge(canonical, existing).Page.Should().Be(10);
    }

    [Fact]
    public void Page_canonical_wins_if_existing_has_no_page()
    {
        var canonical = MakeEnvelope(page: 42);
        var existing  = MakeEnvelope(page: null, dataSource: "5etools");
        EntityMerger.Merge(canonical, existing).Page.Should().Be(42);
    }

    [Fact]
    public void DataSource_is_always_llm_after_merge()
    {
        var canonical = MakeEnvelope(dataSource: "llm");
        var existing  = MakeEnvelope(dataSource: "5etools");
        EntityMerger.Merge(canonical, existing).DataSource.Should().Be("llm");
    }

    [Fact]
    public void Merge_does_not_mutate_inputs()
    {
        var canonical = MakeEnvelope(srd: false);
        var existing  = MakeEnvelope(srd: true, dataSource: "5etools");
        _ = EntityMerger.Merge(canonical, existing);
        canonical.Srd.Should().BeFalse();
        existing.Srd.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "EntityMergerTests" -v minimal
```

Expected: FAIL — `EntityMerger` does not exist yet.

- [ ] **Step 3: Implement EntityMerger**

```csharp
// Features/Ingestion/Entities/EntityMerger.cs
namespace DndMcpAICsharpFun.Features.Ingestion.Entities;

public static class EntityMerger
{
    public static EntityEnvelope Merge(EntityEnvelope canonical, EntityEnvelope existing)
    {
        var type = canonical.Type == EntityType.Class && existing.Type != EntityType.Class
            ? existing.Type
            : canonical.Type;

        var keywords = existing.Keywords.Count >= canonical.Keywords.Count
            ? existing.Keywords
            : canonical.Keywords;

        var page = existing.Page ?? canonical.Page;

        return canonical with
        {
            Type            = type,
            Srd             = existing.Srd,
            Srd52           = existing.Srd52,
            BasicRules2024  = existing.BasicRules2024,
            Keywords        = keywords,
            Page            = page,
            DataSource      = "llm",
        };
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "EntityMergerTests" -v minimal
```

Expected: All EntityMergerTests PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/Entities/EntityMerger.cs DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityMergerTests.cs
git commit -m "feat(entity-merge): add EntityMerger with per-field priority rules"
```

---

## Task 4: Wire Merge into EntityIngestionOrchestrator + Revert Runtime Rewriting

**Files:**

- Modify: `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs`

- [ ] **Step 1: Write failing merge integration test**

Add to `EntityIngestionOrchestratorTests`:

```csharp
[Fact]
public async Task IngestEntitiesAsync_MergesWithExisting5etoolsData()
{
    var tracker = Substitute.For<IIngestionTracker>();
    var record = new IngestionRecord { Id = 1, DisplayName = "Test Book", FileHash = "deadbeef" };
    tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);

    var embeddings = Substitute.For<IEmbeddingService>();
    embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
        .Returns(ci => Task.FromResult<IList<float[]>>(
            Enumerable.Range(0, ci.Arg<IList<string>>().Count).Select(_ => new float[1024]).ToList()));

    // Simulate an existing 5etools point with srd=true
    var existingEnvelopes = new Dictionary<string, EntityEnvelope>();
    var store = Substitute.For<IEntityVectorStore>();
    store.GetByIdsAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
        .Returns(ci =>
        {
            var ids = ci.Arg<IList<string>>();
            // Return existing with srd=true for first entity
            var dict = new Dictionary<string, EntityEnvelope>();
            if (ids.Count > 0)
            {
                var existing = new EntityEnvelope(
                    Id: ids[0], Type: EntityType.Subclass, Name: "X",
                    SourceBook: "TCE", Edition: "Edition2014", Page: 5,
                    FirstAppearedIn: new FirstAppearance("TCE", "Edition2014", 5),
                    RevisedIn: Array.Empty<Revision>(), SettingTags: Array.Empty<string>(),
                    CanonicalText: "", Fields: JsonDocument.Parse("{}").RootElement.Clone(),
                    DataSource: "5etools", Srd: true);
                dict[ids[0]] = existing;
            }
            return Task.FromResult<IReadOnlyDictionary<string, EntityEnvelope>>(dict);
        });

    IList<EntityPoint>? captured = null;
    store.UpsertAsync(Arg.Any<IList<EntityPoint>>(), Arg.Any<CancellationToken>())
        .Returns(ci => { captured = ci.Arg<IList<EntityPoint>>(); return Task.CompletedTask; });

    var canonicalDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical");
    var orchestrator = new EntityIngestionOrchestrator(
        tracker, new CanonicalJsonLoader(), new EntityCanonicalTextDispatcher(),
        new EntityReferenceResolver(), embeddings, store,
        Options.Create(new EntityIngestionOptions { CanonicalDirectory = canonicalDir }),
        NullLogger<EntityIngestionOrchestrator>.Instance);

    await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

    Assert.NotNull(captured);
    // First entity should have srd=true from existing 5etools data
    captured!.Should().Contain(p => p.Envelope.Srd == true);
}
```

- [ ] **Step 2: Replace IngestEntitiesAsync in EntityIngestionOrchestrator.cs**

Replace the entire class body with this clean implementation (removes the runtime ID rewriting and `RewriteBookPrefix` method, adds merge step):

```csharp
public sealed class EntityIngestionOrchestrator(
    IIngestionTracker tracker,
    CanonicalJsonLoader loader,
    EntityCanonicalTextDispatcher textDispatcher,
    EntityReferenceResolver refResolver,
    IEmbeddingService embeddings,
    IEntityVectorStore store,
    IOptions<EntityIngestionOptions> options,
    ILogger<EntityIngestionOrchestrator> logger) : IEntityIngestionOrchestrator
{
    private readonly EntityIngestionOptions _opts = options.Value;

    public async Task IngestEntitiesAsync(int bookId, CancellationToken ct = default)
    {
        var record = await tracker.GetByIdAsync(bookId, ct)
                     ?? throw new InvalidOperationException($"No ingestion record {bookId}");

        var bookSlug = EntityIdSlug
            .For(record.DisplayName, EntityType.Class, "x")
            .Split('.')[0];
        var path = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Canonical JSON not found for book {bookId} at {path}", path);

        var file = await loader.LoadAsync(path, ct);

        foreach (var w in refResolver.Resolve(file.Entities))
            logger.LogWarning("Dangling entity reference: {Source} -> {Target} ({Path})",
                w.SourceEntityId, w.MissingTargetId, w.FieldPath);

        // Pre-fetch existing Qdrant data for all entities to enable per-field merge.
        var entityIds = file.Entities.Select(e => e.Id).ToList();
        var existing = await store.GetByIdsAsync(entityIds, ct);

        await store.DeleteByFileHashAsync(record.FileHash, ct);

        var renderedEnvelopes = new List<EntityEnvelope>(file.Entities.Count);
        var texts = new List<string>(file.Entities.Count);
        foreach (var envelope in file.Entities)
        {
            ct.ThrowIfCancellationRequested();

            // Merge with existing 5etools data if present.
            var merged = existing.TryGetValue(envelope.Id, out var existingEnvelope)
                ? EntityMerger.Merge(envelope, existingEnvelope)
                : envelope;

            string text;
            try
            {
                text = textDispatcher.Render(merged);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping entity {Id} — render failed", merged.Id);
                continue;
            }
            var ds = string.IsNullOrEmpty(merged.DataSource) ? "llm" : merged.DataSource;
            var sourceBook = record.FivetoolsSourceKey ?? merged.SourceBook;
            renderedEnvelopes.Add(merged with { CanonicalText = text, DataSource = ds, SourceBook = sourceBook });
            texts.Add(text);
        }

        IList<float[]> vectors = texts.Count == 0
            ? Array.Empty<float[]>()
            : await embeddings.EmbedAsync(texts, ct);

        var points = new List<EntityPoint>(renderedEnvelopes.Count);
        for (int i = 0; i < renderedEnvelopes.Count; i++)
            points.Add(new EntityPoint(renderedEnvelopes[i], vectors[i], record.FileHash));

        await store.UpsertAsync(points, ct);

        await tracker.MarkEntitiesIngestedAsync(bookId, points.Count, ct);
        logger.LogInformation("Entity ingestion complete: book {BookId}, {Count} entities", bookId, points.Count);
    }
}
```

- [ ] **Step 3: Run all orchestrator tests**

```bash
dotnet test --filter "EntityIngestionOrchestratorTests" -v minimal
```

Expected: All tests PASS including the new merge test.

- [ ] **Step 4: Run full test suite**

```bash
dotnet test -v minimal
```

Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/Entities/EntityIngestionOrchestrator.cs DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs
git commit -m "feat(ingest): wire EntityMerger into orchestrator, remove runtime ID rewriting"
```

---

## Task 5: CanonicalTypeFixerService + Admin Endpoint

**Files:**

- Create: `Features/Admin/CanonicalTypeFixerService.cs`
- Modify: `Features/Admin/FivetoolsAdminEndpoints.cs` (add fix-types route)
- Modify: `DndMcpAICsharpFun.http`
- Modify: `dnd-mcp-api.insomnia.json`
- Test: `DndMcpAICsharpFun.Tests/Entities/Admin/CanonicalTypeFixerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// DndMcpAICsharpFun.Tests/Entities/Admin/CanonicalTypeFixerTests.cs
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;

public class CanonicalTypeFixerTests
{
    [Fact]
    public async Task FixTypesAsync_RewritesEntityTypeFromFivetoolsLookup()
    {
        // Arrange: a canonical JSON with one Class entity that 5etools knows as Subclass
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var canonicalJson = """
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "TCE", "edition": "Edition2014", "fileHash": "", "displayName": "Tasha's" },
          "entities": [{
            "id": "tce.class.circle-of-spores",
            "type": "Class",
            "name": "Circle of Spores",
            "sourceBook": "TCE",
            "edition": "Edition2014",
            "page": 36,
            "firstAppearedIn": { "book": "TCE", "edition": "Edition2014", "page": null },
            "revisedIn": [], "settingTags": [], "canonicalText": "", "fields": {},
            "dataSource": "llm", "srd": false, "srd52": false, "basicRules2024": false, "keywords": []
          }]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(dir, "tce.json"), canonicalJson);

        // Fake 5etools lookup: "Circle of Spores" from TCE is a Subclass
        var fakeEnvelopes = new Dictionary<(string name, string source), EntityType>
        {
            [("circle of spores", "TCE")] = EntityType.Subclass
        };

        var svc = new CanonicalTypeFixerService(dir);

        // Act
        var result = await svc.FixTypesAsync("tce", fakeEnvelopes);

        // Assert
        result.Fixed.Should().Be(1);
        result.Unmatched.Should().Be(0);

        var updated = await File.ReadAllTextAsync(Path.Combine(dir, "tce.json"));
        updated.Should().Contain("\"type\": \"Subclass\"");
        updated.Should().Contain("\"id\": \"tce.subclass.circle-of-spores\"");
        updated.Should().NotContain("tce.class.circle-of-spores");
    }

    [Fact]
    public async Task FixTypesAsync_LeavesUnmatchedEntityUnchanged()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var canonicalJson = """
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "TCE", "edition": "Edition2014", "fileHash": "", "displayName": "Tasha's" },
          "entities": [{
            "id": "tce.class.custom-thing",
            "type": "Class", "name": "Custom Thing", "sourceBook": "TCE",
            "edition": "Edition2014", "page": null,
            "firstAppearedIn": { "book": "TCE", "edition": "Edition2014", "page": null },
            "revisedIn": [], "settingTags": [], "canonicalText": "", "fields": {},
            "dataSource": "llm", "srd": false, "srd52": false, "basicRules2024": false, "keywords": []
          }]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(dir, "tce.json"), canonicalJson);

        var svc = new CanonicalTypeFixerService(dir);
        var result = await svc.FixTypesAsync("tce", new Dictionary<(string, string), EntityType>());

        result.Fixed.Should().Be(0);
        result.Unmatched.Should().Be(1);

        var content = await File.ReadAllTextAsync(Path.Combine(dir, "tce.json"));
        content.Should().Contain("\"id\": \"tce.class.custom-thing\"");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "CanonicalTypeFixerTests" -v minimal
```

Expected: FAIL — `CanonicalTypeFixerService` does not exist.

- [ ] **Step 3: Implement CanonicalTypeFixerService**

```csharp
// Features/Admin/CanonicalTypeFixerService.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Admin;

public sealed class CanonicalTypeFixerService(string canonicalDirectory)
{
    public record FixResult(int Fixed, int Unmatched, int CrossRefsUpdated);

    public async Task<FixResult> FixTypesAsync(
        string bookSlug,
        IReadOnlyDictionary<(string name, string source), EntityType> fivetoolsLookup,
        CancellationToken ct = default)
    {
        var path = Path.Combine(canonicalDirectory, bookSlug + ".json");
        if (!File.Exists(path)) throw new FileNotFoundException(path);

        var json = await File.ReadAllTextAsync(path, ct);
        var doc = JsonNode.Parse(json)!;
        var entities = doc["entities"]!.AsArray();

        int fixed_ = 0, unmatched = 0, xrefUpdated = 0;

        // Build rename map: oldId -> newId
        var renames = new Dictionary<string, string>();
        foreach (var entity in entities)
        {
            var name   = entity!["name"]!.GetValue<string>();
            var source = entity["sourceBook"]!.GetValue<string>();
            var key    = (name.ToLowerInvariant(), source);

            if (!fivetoolsLookup.TryGetValue(key, out var correctType))
            {
                unmatched++;
                continue;
            }

            var currentType = entity["type"]!.GetValue<string>();
            if (string.Equals(currentType, correctType.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            var oldId = entity["id"]!.GetValue<string>();
            var newId = ReplaceTypeSlug(oldId, correctType);
            if (oldId == newId) continue;

            renames[oldId] = newId;
            entity["type"] = correctType.ToString();
            entity["id"] = newId;
            fixed_++;
        }

        if (renames.Count == 0)
        {
            await File.WriteAllTextAsync(path, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
            return new FixResult(fixed_, unmatched, 0);
        }

        // Rewrite cross-references: string-replace all old IDs in the serialized JSON
        var updated = doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        foreach (var (oldId, newId) in renames)
        {
            var before = updated.Length;
            updated = updated.Replace($"\"{oldId}\"", $"\"{newId}\"");
            if (updated.Length != before) xrefUpdated++;
        }

        await File.WriteAllTextAsync(path, updated, ct);
        return new FixResult(fixed_, unmatched, xrefUpdated);
    }

    private static string ReplaceTypeSlug(string id, EntityType newType)
    {
        var parts = id.Split('.');
        if (parts.Length < 3) return id;
        parts[1] = newType.ToString().ToLowerInvariant();
        return string.Join('.', parts);
    }
}
```

- [ ] **Step 4: Add fix-types endpoint to FivetoolsAdminEndpoints**

In `Features/Admin/FivetoolsAdminEndpoints.cs`, add inside `MapFivetoolsAdmin`:

```csharp
group.MapPost("/canonical/fix-types", FixTypes);
```

And add the handler method:

```csharp
private static async Task<IResult> FixTypes(
    string book,
    FivetoolsIngestionService fivetools,
    IOptions<EntityIngestionOptions> opts,
    CancellationToken ct)
{
    var canonicalDir = opts.Value.CanonicalDirectory;
    var path = Path.Combine(canonicalDir, book + ".json");
    if (!File.Exists(path)) return Results.NotFound($"No canonical JSON for book '{book}'");

    // Build 5etools lookup: (name.lower, sourceBook) -> EntityType
    var lookup = await fivetools.BuildTypeLookupAsync(ct);

    var svc = new CanonicalTypeFixerService(canonicalDir);
    var result = await svc.FixTypesAsync(book, lookup, ct);

    return Results.Ok(new { result.Fixed, result.Unmatched, result.CrossRefsUpdated });
}
```

Add `BuildTypeLookupAsync` to `FivetoolsIngestionService`:

```csharp
public async Task<IReadOnlyDictionary<(string name, string source), EntityType>> BuildTypeLookupAsync(
    CancellationToken ct = default)
{
    var lookup = new Dictionary<(string, string), EntityType>();
    foreach (var entry in FivetoolsSourceRegistry.AllEntries)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(entry.RelativePath)) continue;
        if (!Mappers.TryGetValue(entry.EntityType, out var mapper)) continue;

        await using var stream = File.OpenRead(entry.RelativePath);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty(entry.JsonArrayKey, out var arr)
            || arr.ValueKind != JsonValueKind.Array) continue;

        foreach (var item in arr.EnumerateArray())
        {
            var envelope = mapper.Map(item);
            if (envelope is null) continue;
            var key = (envelope.Name.ToLowerInvariant(), envelope.SourceBook);
            lookup.TryAdd(key, envelope.Type);
        }
    }
    return lookup;
}
```

- [ ] **Step 5: Register CanonicalTypeFixerService in DI (if needed)**

Check `Extensions/ServiceCollectionExtensions.cs` — `CanonicalTypeFixerService` takes `canonicalDirectory` as constructor arg so it is instantiated per-request from `EntityIngestionOptions`, no DI registration needed.

Add `using Microsoft.Extensions.Options;` to `FivetoolsAdminEndpoints.cs` if not present.

- [ ] **Step 6: Run tests**

```bash
dotnet test --filter "CanonicalTypeFixerTests" -v minimal
```

Expected: All PASS.

- [ ] **Step 7: Add endpoint to .http and insomnia files**

In `DndMcpAICsharpFun.http`, add:

```http
### Fix canonical entity types for a book using 5etools reference
POST {{baseUrl}}/admin/canonical/fix-types?book=tce
```

In `dnd-mcp-api.insomnia.json`, add a matching POST request entry in the admin folder.

- [ ] **Step 8: Run full test suite and build**

```bash
dotnet build && dotnet test -v minimal
```

Expected: All PASS.

- [ ] **Step 9: Commit**

```bash
git add Features/Admin/CanonicalTypeFixerService.cs Features/Admin/FivetoolsAdminEndpoints.cs Features/Ingestion/FivetoolsIngestion/FivetoolsIngestionService.cs DndMcpAICsharpFun.Tests/Entities/Admin/CanonicalTypeFixerTests.cs DndMcpAICsharpFun.http dnd-mcp-api.insomnia.json
git commit -m "feat(admin): add POST /admin/canonical/fix-types endpoint and CanonicalTypeFixerService"
```

---

## Task 6: Update Extraction Prompt for Correct EntityType

**Files:**

- Modify: `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs`

- [ ] **Step 1: Write failing test**

Add to `ExtractionPromptBuilderTests`:

```csharp
[Fact]
public void System_prompt_includes_entity_type_classification_guidance()
{
    var b = new ExtractionPromptBuilder();
    foreach (var type in Enum.GetValues<EntityType>())
    {
        var prompt = b.BuildSystemPrompt("TCE", "Edition2014", type);
        prompt.Should().Contain("Subclass", because: "all prompts should list Subclass as a valid type");
        prompt.Should().Contain("Class is a last resort", because: "prompts must warn against defaulting to Class");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test --filter "System_prompt_includes_entity_type_classification_guidance" -v minimal
```

Expected: FAIL.

- [ ] **Step 3: Add type routing guidance to BuildSystemPrompt**

In `ExtractionPromptBuilder.cs`, after the existing `sb.AppendLine("Type routing guidance:");` block and before the `switch (type)`, add:

```csharp
sb.AppendLine();
sb.AppendLine("IMPORTANT — EntityType classification rules:");
sb.AppendLine("Valid types: Class, Subclass, Spell, Monster, Race, Subrace, Background, Feat, Item, MagicItem, Weapon, Armor, God, Trap, Condition, DiseasePoison, VehicleMount, Rule, Lore, Plane.");
sb.AppendLine("- Use `Subclass` for named subclass entries (e.g. Path of Wild Magic, Circle of Spores, Oath of Glory).");
sb.AppendLine("- Use `Spell` for any named spell entry with casting time, range, duration, and components.");
sb.AppendLine("- Use `Feat` for player feats with prerequisites.");
sb.AppendLine("- Use `Rule` for metamagic options, fighting styles, eldritch invocations, maneuvers, and other optional mechanical add-ons.");
sb.AppendLine("- Use `Class` ONLY for a full base class entry (e.g. Barbarian, Fighter). Class is a last resort — prefer a more specific type.");
sb.AppendLine("If uncertain, pick the most specific applicable type over Class.");
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "ExtractionPromptBuilderTests" -v minimal
```

Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs
git commit -m "feat(extraction): add EntityType classification guidance to system prompt"
```

---

## Task 7: Migration — Re-ingest All Books with Correct IDs

This task is operational (no code changes). Run after all code tasks are deployed.

- [ ] **Step 1: Deploy code**

```bash
docker compose up --build -d
```

- [ ] **Step 2: Re-import 5etools data with deterministic UUIDs**

```http
POST http://localhost:5101/admin/5etools/import
```

- [ ] **Step 3: Fix types in each canonical book**

```http
POST http://localhost:5101/admin/canonical/fix-types?book=phb14
POST http://localhost:5101/admin/canonical/fix-types?book=tce
POST http://localhost:5101/admin/canonical/fix-types?book=dungeon-master-s-guide
```

Review the JSON diffs in `data/canonical/` for each book.

- [ ] **Step 4: Validate canonical corpus**

```http
POST http://localhost:5101/admin/canonical/validate
```

Expected: 200 OK (no failures).

- [ ] **Step 5: Re-ingest each canonical book**

```http
POST http://localhost:5101/admin/books/1/ingest-entities
POST http://localhost:5101/admin/books/2/ingest-entities
POST http://localhost:5101/admin/books/3/ingest-entities
```

- [ ] **Step 6: Verify no duplicates in search results**

```http
GET http://localhost:5101/retrieval/entities/search?q=circle+of+spores&topK=5
```

Expected: Single `tce.subclass.circle-of-spores` entry, no duplicates.
