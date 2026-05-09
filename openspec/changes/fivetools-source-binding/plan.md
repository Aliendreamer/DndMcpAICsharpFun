# 5etools Source Binding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bind each `IngestionRecord` to a 5etools source key, derive edition/group/year from it at runtime, normalise Qdrant `sourceBook` across both pipelines, and add SRD availability flags (`srd`, `srd52`, `basicRules2024`) to entities.

**Architecture:** A singleton `BookSourceRegistry` loads `5etools/books.json` once at startup and resolves source keys to metadata (group, year, display abbreviation) and human intents ("core books", "2024") to source key lists. `IngestionRecord` gains a nullable `FivetoolsSourceKey` column. During extraction and ingestion the source key is propagated to canonical JSON and Qdrant payload so both pipelines write consistent `sourceBook` values. SRD flags flow from 5etools JSON through `EntityEnvelope` to Qdrant payload and are exposed as optional search filter parameters.

**Tech Stack:** ASP.NET Core minimal API (.NET 10), EF Core + SQLite, Qdrant (gRPC client), xUnit + FluentAssertions + NSubstitute

---

## File Map

**New files:**
- `Features/Ingestion/FivetoolsIngestion/BookSourceRegistry.cs` — singleton registry
- `DndMcpAICsharpFun.Tests/Ingestion/FivetoolsIngestion/BookSourceRegistryTests.cs`

**Modified files:**
- `Infrastructure/Sqlite/IngestionRecord.cs` — add `FivetoolsSourceKey`
- `Domain/Entities/EntityEnvelope.cs` — add `Srd`, `Srd52`, `BasicRules2024`
- `Infrastructure/Qdrant/EntityPayloadFields.cs` — add SRD constants
- `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` — add SRD fields to entity index
- `Features/VectorStore/Entities/IEntityVectorStore.cs` — add SRD fields to `EntityFilters`
- `Features/VectorStore/Entities/QdrantEntityVectorStore.cs` — write/read SRD in payload, SRD in `BuildFilter`
- `Features/Ingestion/FivetoolsIngestion/FivetoolsMapperBase.cs` — read srd/srd52/basicRules2024 from JSON
- `Features/Admin/BooksAdminEndpoints.cs` — parse `fivetoolsSourceKey`, return suggestions
- `Features/Admin/FivetoolsAdminEndpoints.cs` — add `GET /admin/5etools/sources`
- `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` — inject registry, propagate source key + edition
- `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs` — normalise `SourceBook` to source key
- `Features/Retrieval/Entities/EntitySearchQuery.cs` — add `Srd`, `Srd52`, `BasicRules2024`
- `Features/Retrieval/Entities/EntityRetrievalEndpoints.cs` — accept SRD query params
- `Features/Retrieval/Entities/EntityRetrievalService.cs` — pass SRD to `EntityFilters`
- `Extensions/ServiceCollectionExtensions.cs` — register `BookSourceRegistry`
- `DndMcpAICsharpFun.http` — new endpoint examples
- `dnd-mcp-api.insomnia.json` — sync

---

## Task 1: BookSourceRegistry

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/BookSourceRegistry.cs`
- Create: `DndMcpAICsharpFun.Tests/Ingestion/FivetoolsIngestion/BookSourceRegistryTests.cs`

- [ ] **Step 1.1: Write failing tests**

```csharp
// DndMcpAICsharpFun.Tests/Ingestion/FivetoolsIngestion/BookSourceRegistryTests.cs
using System.IO;
using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;

public class BookSourceRegistryTests : IDisposable
{
    private readonly string _tmpPath;

    public BookSourceRegistryTests()
    {
        _tmpPath = Path.GetTempFileName();
        var json = """
        {
          "book": [
            { "id": "PHB",  "name": "Player's Handbook (2014)", "source": "PHB",  "group": "core",       "published": "2014-08-19" },
            { "id": "XPHB", "name": "Player's Handbook (2024)", "source": "XPHB", "group": "core",       "published": "2024-09-17" },
            { "id": "TCE",  "name": "Tasha's Cauldron of Everything", "source": "TCE", "group": "supplement", "published": "2020-11-17" }
          ]
        }
        """;
        File.WriteAllText(_tmpPath, json);
    }

    public void Dispose() => File.Delete(_tmpPath);

    [Fact]
    public void TryGetBook_KnownKey_ReturnsInfo()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        var info = sut.TryGetBook("PHB");
        info.Should().NotBeNull();
        info!.SourceKey.Should().Be("PHB");
        info.Group.Should().Be("core");
        info.PublishedYear.Should().Be(2014);
        info.DisplayAbbr.Should().Be("PHB'14");
    }

    [Fact]
    public void TryGetBook_XPrefixedKey_StripsXInDisplayAbbr()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        var info = sut.TryGetBook("XPHB");
        info!.DisplayAbbr.Should().Be("PHB'24");
    }

    [Fact]
    public void TryGetBook_SupplementKey_AppendsYear()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        var info = sut.TryGetBook("TCE");
        info!.DisplayAbbr.Should().Be("TCE'20");
    }

    [Fact]
    public void TryGetBook_UnknownKey_ReturnsNull()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.TryGetBook("HOMEBREW").Should().BeNull();
    }

    [Fact]
    public void GetByGroup_Core_ReturnsCoreKeys()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        var keys = sut.GetByGroup("core");
        keys.Should().BeEquivalentTo(["PHB", "XPHB"]);
    }

    [Fact]
    public void GetByGroup_Unknown_ReturnsEmpty()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.GetByGroup("does-not-exist").Should().BeEmpty();
    }

    [Fact]
    public void ResolveIntent_CoreBooks_ReturnsCoreKeys()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.ResolveIntent("core books").Should().BeEquivalentTo(["PHB", "XPHB"]);
        sut.ResolveIntent("core").Should().BeEquivalentTo(["PHB", "XPHB"]);
    }

    [Fact]
    public void ResolveIntent_2024_Returns2024PlusKeys()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.ResolveIntent("2024").Should().BeEquivalentTo(["XPHB"]);
    }

    [Fact]
    public void ResolveIntent_2014_Returns2014Keys()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.ResolveIntent("2014").Should().BeEquivalentTo(["PHB"]);
    }

    [Fact]
    public void ResolveIntent_Srd_ReturnsSentinel()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.ResolveIntent("srd").Should().BeEquivalentTo(["srd52"]);
        sut.ResolveIntent("free rules").Should().BeEquivalentTo(["srd52"]);
    }

    [Fact]
    public void ResolveIntent_Unknown_ReturnsEmpty()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.ResolveIntent("gibberish").Should().BeEmpty();
    }

    [Fact]
    public void MissingBooksJson_DoesNotThrow_EmptyRegistry()
    {
        var sut = new BookSourceRegistry("/nonexistent/path/books.json");
        sut.TryGetBook("PHB").Should().BeNull();
        sut.GetByGroup("core").Should().BeEmpty();
    }

    [Fact]
    public void SuggestByName_PartialMatch_ReturnsSuggestions()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        var suggestions = sut.SuggestByName("Player's Handbook");
        suggestions.Should().Contain("PHB");
    }
}
```

- [ ] **Step 1.2: Run tests to confirm they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "BookSourceRegistryTests" --no-build 2>&1 | tail -5
```
Expected: build error — `BookSourceRegistry` not found.

- [ ] **Step 1.3: Create `BookSourceRegistry.cs`**

```csharp
// Features/Ingestion/FivetoolsIngestion/BookSourceRegistry.cs
using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public sealed record FivetoolsBookInfo(
    string SourceKey,
    string Name,
    string Group,
    int PublishedYear,
    string DisplayAbbr);

public sealed class BookSourceRegistry
{
    private readonly IReadOnlyDictionary<string, FivetoolsBookInfo> _byKey;
    private readonly IReadOnlyList<FivetoolsBookInfo> _all;

    public BookSourceRegistry(string booksJsonPath = "5etools/books.json")
    {
        if (!File.Exists(booksJsonPath))
        {
            _byKey = new Dictionary<string, FivetoolsBookInfo>(StringComparer.OrdinalIgnoreCase);
            _all = Array.Empty<FivetoolsBookInfo>();
            return;
        }

        using var stream = File.OpenRead(booksJsonPath);
        var doc = JsonDocument.Parse(stream);
        var entries = new List<FivetoolsBookInfo>();

        foreach (var b in doc.RootElement.GetProperty("book").EnumerateArray())
        {
            var id        = b.GetProperty("id").GetString()!;
            var name      = b.GetProperty("name").GetString()!;
            var group     = b.TryGetProperty("group", out var g) ? g.GetString()! : "other";
            var published = b.TryGetProperty("published", out var p) ? p.GetString()! : "2000-01-01";
            var year      = int.Parse(published.AsSpan(0, 4));
            entries.Add(new FivetoolsBookInfo(id, name, group, year, ComputeDisplayAbbr(id, year)));
        }

        _byKey = entries.ToDictionary(e => e.SourceKey, StringComparer.OrdinalIgnoreCase);
        _all   = entries;
    }

    public FivetoolsBookInfo? TryGetBook(string sourceKey)
        => _byKey.TryGetValue(sourceKey, out var info) ? info : null;

    public IReadOnlyList<FivetoolsBookInfo> GetAll() => _all;

    public IReadOnlyList<string> GetByGroup(string group)
        => _all.Where(b => string.Equals(b.Group, group, StringComparison.OrdinalIgnoreCase))
               .Select(b => b.SourceKey).ToList();

    public IReadOnlyList<string> ResolveIntent(string intent)
        => intent.Trim().ToLowerInvariant() switch
        {
            "core" or "core books"         => GetByGroup("core"),
            "supplement" or "supplements"  => GetByGroup("supplement"),
            "setting" or "settings"        => GetByGroup("setting"),
            "2014" or "5e"                 => _all.Where(b => b.PublishedYear < 2020).Select(b => b.SourceKey).ToList(),
            "2024" or "5.5e"               => _all.Where(b => b.PublishedYear >= 2024).Select(b => b.SourceKey).ToList(),
            "srd" or "free rules"          => (IReadOnlyList<string>)["srd52"],
            _                              => Array.Empty<string>()
        };

    public IReadOnlyList<string> SuggestByName(string displayName, int top = 3)
        => _all
            .Select(b => (b.SourceKey, Score: NameSimilarity(displayName, b.Name)))
            .Where(x => x.Score > 0.2)
            .OrderByDescending(x => x.Score)
            .Take(top)
            .Select(x => x.SourceKey)
            .ToList();

    private static double NameSimilarity(string a, string b)
    {
        var wordsA = Tokenize(a).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wordsB = Tokenize(b).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var intersection = wordsA.Count(w => wordsB.Contains(w));
        var union = wordsA.Union(wordsB, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static IEnumerable<string> Tokenize(string s)
        => s.ToLowerInvariant().Split([' ', '-', '\'', '(', ')', ':'], StringSplitOptions.RemoveEmptyEntries);

    private static string ComputeDisplayAbbr(string sourceKey, int year)
    {
        var key = sourceKey.StartsWith('X') ? sourceKey[1..] : sourceKey;
        return $"{key}'{year % 100:D2}";
    }
}
```

- [ ] **Step 1.4: Run tests to confirm they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "BookSourceRegistryTests" -v minimal
```
Expected: all green.

- [ ] **Step 1.5: Register as singleton in `ServiceCollectionExtensions.AddIngestionPipeline`**

In `Extensions/ServiceCollectionExtensions.cs`, inside `AddIngestionPipeline`, add before `return services;`:
```csharp
services.AddSingleton<BookSourceRegistry>();
```

- [ ] **Step 1.6: Build to confirm no compilation errors**

```bash
dotnet build -warnaserror 2>&1 | tail -10
```
Expected: `Build succeeded`.

- [ ] **Step 1.7: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/BookSourceRegistry.cs \
        DndMcpAICsharpFun.Tests/Ingestion/FivetoolsIngestion/BookSourceRegistryTests.cs \
        Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(registry): add BookSourceRegistry singleton loading 5etools/books.json"
```

---

## Task 2: IngestionRecord FivetoolsSourceKey Column

**Files:**
- Modify: `Infrastructure/Sqlite/IngestionRecord.cs`
- Run migration command to generate `Migrations/` files

- [ ] **Step 2.1: Add property to `IngestionRecord`**

In `Infrastructure/Sqlite/IngestionRecord.cs`, add after `public BookType BookType`:
```csharp
[MaxLength(20)]
public string? FivetoolsSourceKey { get; set; }
```

- [ ] **Step 2.2: Generate EF Core migration**

```bash
dotnet ef migrations add AddFivetoolsSourceKeyToIngestionRecord
```
Expected: new file created in `Migrations/`.

- [ ] **Step 2.3: Verify migration SQL is additive (nullable column)**

```bash
cat Migrations/*AddFivetoolsSourceKey*.cs | grep -A5 "migrationBuilder.AddColumn"
```
Expected: `nullable: true` in the column definition.

- [ ] **Step 2.4: Apply migration to local DB**

```bash
dotnet ef database update
```
Expected: `Done.`

- [ ] **Step 2.5: Commit**

```bash
git add Infrastructure/Sqlite/IngestionRecord.cs Migrations/
git commit -m "feat(db): add nullable FivetoolsSourceKey column to IngestionRecords"
```

---

## Task 3: EntityEnvelope SRD Flags

**Files:**
- Modify: `Domain/Entities/EntityEnvelope.cs`

- [ ] **Step 3.1: Add optional SRD properties to the record**

Current tail of `EntityEnvelope`:
```csharp
    JsonElement Fields,
    string DataSource = "");
```

Replace with:
```csharp
    JsonElement Fields,
    string DataSource = "",
    bool Srd = false,
    bool Srd52 = false,
    bool BasicRules2024 = false);
```

- [ ] **Step 3.2: Build and confirm all existing callsites still compile**

```bash
dotnet build -warnaserror 2>&1 | tail -10
```
Expected: `Build succeeded` — all existing positional callers pass no SRD args, which is fine because the new params are optional with defaults.

- [ ] **Step 3.3: Run existing tests to confirm no regressions**

```bash
dotnet test DndMcpAICsharpFun.Tests -v minimal 2>&1 | tail -15
```
Expected: all previously passing tests still green.

- [ ] **Step 3.4: Commit**

```bash
git add Domain/Entities/EntityEnvelope.cs
git commit -m "feat(entity): add Srd/Srd52/BasicRules2024 optional flags to EntityEnvelope"
```

---

## Task 4: Qdrant SRD Payload Fields

**Files:**
- Modify: `Infrastructure/Qdrant/EntityPayloadFields.cs`
- Modify: `Infrastructure/Qdrant/QdrantCollectionInitializer.cs`
- Modify: `Features/VectorStore/Entities/IEntityVectorStore.cs`
- Modify: `Features/VectorStore/Entities/QdrantEntityVectorStore.cs`

- [ ] **Step 4.1: Add SRD constants to `EntityPayloadFields`**

In `Infrastructure/Qdrant/EntityPayloadFields.cs`, add at the end of the class body before the closing `}`:
```csharp
public const string Srd            = "srd";
public const string Srd52          = "srd52";
public const string BasicRules2024 = "basic_rules_2024";
```

- [ ] **Step 4.2: Add SRD index fields to `CreateEntityPayloadIndexesAsync`**

In `Infrastructure/Qdrant/QdrantCollectionInitializer.cs`, add the three new constants to the `keywordFields` array inside `CreateEntityPayloadIndexesAsync`:
```csharp
string[] keywordFields =
[
    EntityPayloadFields.Type,
    EntityPayloadFields.SourceBook,
    EntityPayloadFields.Edition,
    EntityPayloadFields.BookType,
    EntityPayloadFields.SettingTags,
    EntityPayloadFields.Keywords,
    EntityPayloadFields.DamageType,
    EntityPayloadFields.FirstBook,
    EntityPayloadFields.FirstEdition,
    EntityPayloadFields.FileHash,
    EntityPayloadFields.Srd,            // new
    EntityPayloadFields.Srd52,          // new
    EntityPayloadFields.BasicRules2024, // new
];
```

- [ ] **Step 4.3: Add SRD params to `EntityFilters`**

In `Features/VectorStore/Entities/IEntityVectorStore.cs`, update `EntityFilters`:
```csharp
public sealed record EntityFilters(
    EntityType? Type = null,
    string? SourceBook = null,
    string? Edition = null,
    string? BookType = null,
    string? SettingTag = null,
    string? Keyword = null,
    double? CrNumericLte = null,
    double? CrNumericGte = null,
    int? SpellLevel = null,
    string? DamageType = null,
    bool? Srd = null,
    bool? Srd52 = null,
    bool? BasicRules2024 = null);
```

- [ ] **Step 4.4: Write SRD flags to Qdrant payload in `ToPoint`**

In `Features/VectorStore/Entities/QdrantEntityVectorStore.cs`, inside `ToPoint`, after the existing `DataSource` line add:
```csharp
[EntityPayloadFields.DataSource]    = p.Envelope.DataSource,
[EntityPayloadFields.Srd]           = p.Envelope.Srd ? "true" : "false",
[EntityPayloadFields.Srd52]         = p.Envelope.Srd52 ? "true" : "false",
[EntityPayloadFields.BasicRules2024]= p.Envelope.BasicRules2024 ? "true" : "false",
```
(replace the existing `DataSource` line with the block above so all four are together)

- [ ] **Step 4.5: Read SRD flags back in `ToEnvelope`**

In `Features/VectorStore/Entities/QdrantEntityVectorStore.cs`, find `ToEnvelope`. Look at how the existing parameters are read from `p` (a `MapField<string, Value>`). After `DataSource:` (the last existing parameter), add using the same `p.TryGetValue(…)` pattern already used in that method:
```csharp
Srd:            p.TryGetValue(EntityPayloadFields.Srd,            out var srdV)   && srdV.StringValue   == "true",
Srd52:          p.TryGetValue(EntityPayloadFields.Srd52,          out var srd52V) && srd52V.StringValue  == "true",
BasicRules2024: p.TryGetValue(EntityPayloadFields.BasicRules2024, out var brV)    && brV.StringValue     == "true");
```
(replace the existing `DataSource` closing `)` with this block)

- [ ] **Step 4.6: Filter by SRD flags in `BuildFilter`**

In `Features/VectorStore/Entities/QdrantEntityVectorStore.cs`, at the end of `BuildFilter` just before `if (must.Count == 0) return null;`:
```csharp
if (f.Srd == true)            must.Add(KW(EntityPayloadFields.Srd, "true"));
if (f.Srd52 == true)          must.Add(KW(EntityPayloadFields.Srd52, "true"));
if (f.BasicRules2024 == true) must.Add(KW(EntityPayloadFields.BasicRules2024, "true"));
```

- [ ] **Step 4.7: Build and run tests**

```bash
dotnet build -warnaserror 2>&1 | tail -5
dotnet test DndMcpAICsharpFun.Tests -v minimal 2>&1 | tail -15
```
Expected: build clean, all tests green.

- [ ] **Step 4.8: Commit**

```bash
git add Infrastructure/Qdrant/EntityPayloadFields.cs \
        Infrastructure/Qdrant/QdrantCollectionInitializer.cs \
        Features/VectorStore/Entities/IEntityVectorStore.cs \
        Features/VectorStore/Entities/QdrantEntityVectorStore.cs
git commit -m "feat(qdrant): add srd/srd52/basicRules2024 payload fields and filters to dnd_entities"
```

---

## Task 5: FivetoolsMapper SRD Reading

**Files:**
- Modify: `Features/Ingestion/FivetoolsIngestion/FivetoolsMapperBase.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsMappersTests.cs`

- [ ] **Step 5.1: Write a failing test for SRD flag reading**

In `DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsMappersTests.cs`, add a new test at the end of the class:
```csharp
[Fact]
public void MapSpell_WithSrd52Flag_SetsSrd52True()
{
    var mapper = new FivetoolsSpellMapper();
    var json = """{"name":"Fireball","source":"XPHB","page":274,"srd52":true}""";
    var element = JsonDocument.Parse(json).RootElement;
    var envelope = mapper.Map(element);
    envelope.Should().NotBeNull();
    envelope!.Srd52.Should().BeTrue();
    envelope.Srd.Should().BeFalse();
}

[Fact]
public void MapMonster_WithSrdFlag_SetsSrdTrue()
{
    var mapper = new FivetoolsMonsterMapper();
    var json = """{"name":"Aboleth","source":"MM","page":13,"srd":true,"cr":"10"}""";
    var element = JsonDocument.Parse(json).RootElement;
    var envelope = mapper.Map(element);
    envelope!.Srd.Should().BeTrue();
    envelope.Srd52.Should().BeFalse();
}
```

- [ ] **Step 5.2: Run to confirm the tests fail**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "MapSpell_WithSrd52Flag|MapMonster_WithSrdFlag" -v minimal
```
Expected: FAIL — `envelope.Srd52` is false when it should be true.

- [ ] **Step 5.3: Update `FivetoolsMapperBase.Map` to read SRD flags**

In `Features/Ingestion/FivetoolsIngestion/FivetoolsMapperBase.cs`, find the `return new EntityEnvelope(` block and add the three SRD parameters after `DataSource: "5etools"`:
```csharp
return new EntityEnvelope(
    Id: id,
    Type: EntityType,
    Name: name,
    SourceBook: source,
    Edition: edition,
    Page: page,
    FirstAppearedIn: new FirstAppearance(source, edition, page),
    RevisedIn: Array.Empty<Revision>(),
    SettingTags: Array.Empty<string>(),
    CanonicalText: "",
    Fields: BuildFields(entry),
    DataSource: "5etools",
    Srd:            entry.TryGetProperty("srd",            out var srd)            && srd.ValueKind  == JsonValueKind.True,
    Srd52:          entry.TryGetProperty("srd52",          out var srd52)          && srd52.ValueKind == JsonValueKind.True,
    BasicRules2024: entry.TryGetProperty("basicRules2024", out var br2024)         && br2024.ValueKind == JsonValueKind.True);
```

- [ ] **Step 5.4: Run tests to confirm they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "MapSpell_WithSrd52Flag|MapMonster_WithSrdFlag" -v minimal
```
Expected: both green.

- [ ] **Step 5.5: Run full test suite**

```bash
dotnet test DndMcpAICsharpFun.Tests -v minimal 2>&1 | tail -10
```

- [ ] **Step 5.6: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/FivetoolsMapperBase.cs \
        DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsMappersTests.cs
git commit -m "feat(mapper): read srd/srd52/basicRules2024 flags from 5etools entity JSON"
```

---

## Task 6: Registration Endpoint + GET /admin/5etools/sources

**Files:**
- Modify: `Features/Admin/BooksAdminEndpoints.cs`
- Modify: `Features/Admin/FivetoolsAdminEndpoints.cs`
- Modify: `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs`

- [ ] **Step 6.1: Write failing tests for the new registration behaviour**

In `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs`, add:
```csharp
[Fact]
public async Task RegisterBook_WithValidSourceKey_StoresKey()
{
    // Arrange — same boilerplate as existing RegisterBook tests
    var tracker  = Substitute.For<IIngestionTracker>();
    var registry = new BookSourceRegistry("5etools/books.json"); // reads real file
    tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
        .Returns(ci => { var r = ci.Arg<IngestionRecord>(); r.Id = 1; return r; });

    var content = new MultipartFormDataContent();
    content.Add(new StringContent("Edition2014"), "version");
    content.Add(new StringContent("Player's Handbook"),   "displayName");
    content.Add(new StringContent("Core"), "bookType");
    content.Add(new StringContent("PHB"),  "fivetoolsSourceKey");
    content.Add(new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 }), "file", "phb.pdf");

    // Act
    var app = BuildTestApp(tracker);
    var response = await app.GetTestClient().PostAsync("/admin/books/register", content);

    // Assert
    response.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
    await tracker.Received(1).CreateAsync(
        Arg.Is<IngestionRecord>(r => r.FivetoolsSourceKey == "PHB"),
        Arg.Any<CancellationToken>());
}

[Fact]
public async Task RegisterBook_WithUnknownSourceKey_Returns422()
{
    var tracker  = Substitute.For<IIngestionTracker>();
    var registry = new BookSourceRegistry("5etools/books.json");

    var content = new MultipartFormDataContent();
    content.Add(new StringContent("Edition2014"), "version");
    content.Add(new StringContent("Some Book"),   "displayName");
    content.Add(new StringContent("Core"), "bookType");
    content.Add(new StringContent("NOTABOOK"), "fivetoolsSourceKey");
    content.Add(new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 }), "file", "book.pdf");

    var app = BuildTestApp(tracker);
    var response = await app.GetTestClient().PostAsync("/admin/books/register", content);

    response.StatusCode.Should().Be(System.Net.HttpStatusCode.UnprocessableEntity);
}

[Fact]
public async Task RegisterBook_NoSourceKey_ResponseIncludesSuggestedSources()
{
    var tracker = Substitute.For<IIngestionTracker>();
    tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
        .Returns(ci => { var r = ci.Arg<IngestionRecord>(); r.Id = 2; return r; });

    var content = new MultipartFormDataContent();
    content.Add(new StringContent("Edition2014"), "version");
    content.Add(new StringContent("Player's Handbook"),   "displayName");
    content.Add(new StringContent("Core"), "bookType");
    content.Add(new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 }), "file", "phb.pdf");

    var app = BuildTestApp(tracker);
    var response = await app.GetTestClient().PostAsync("/admin/books/register", content);

    response.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
    var body = await response.Content.ReadAsStringAsync();
    body.Should().Contain("suggestedSources");
}
```

(Add `BookSourceRegistry` as a parameter to `BuildTestApp` or construct it directly in those tests — look at how other `BooksAdminEndpointsTests` build the test app and follow the same pattern.)

- [ ] **Step 6.2: Run to confirm tests fail**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "RegisterBook_WithValidSourceKey|RegisterBook_WithUnknownSourceKey|RegisterBook_NoSourceKey_Response" -v minimal
```
Expected: compilation error or FAIL.

- [ ] **Step 6.3: Add `RegisterBookResponse` record and update `RegisterBook` in `BooksAdminEndpoints.cs`**

At the top of `Features/Admin/BooksAdminEndpoints.cs` (inside the class, before `MapBooksAdmin`):
```csharp
private sealed record RegisterBookResponse(
    IngestionRecord Record,
    IReadOnlyList<string> SuggestedSources);
```

In `RegisterBook`, add `BookSourceRegistry registry` to the method signature:
```csharp
private static async Task<IResult> RegisterBook(
    HttpContext httpContext,
    IIngestionTracker tracker,
    BookSourceRegistry registry,
    IOptions<IngestionOptions> ingestionOptions,
    ILogger<RegisterBookRequest> logger,
    CancellationToken ct)
```

In the multipart section switch, add:
```csharp
case "fivetoolsSourceKey": fivetoolsSourceKey = value; break;
```
(Declare `string? fivetoolsSourceKey = null;` alongside the other string fields at the top of the method.)

After the `bookType` parsing and before `var record = new IngestionRecord`, add validation:
```csharp
if (fivetoolsSourceKey is not null && registry.TryGetBook(fivetoolsSourceKey) is null)
    return Results.UnprocessableEntity(
        $"Unknown fivetoolsSourceKey '{fivetoolsSourceKey}'. " +
        $"Call GET /admin/5etools/sources for valid values.");
```

Set the key on the record:
```csharp
var record = new IngestionRecord
{
    FilePath          = filePath,
    FileName          = originalFileName,
    FileHash          = string.Empty,
    Version           = parsedVersion.ToString(),
    DisplayName       = displayName,
    Status            = IngestionStatus.Pending,
    BookType          = bookType,
    FivetoolsSourceKey = fivetoolsSourceKey,
};
```

Change the return to include suggestions:
```csharp
var created = await tracker.CreateAsync(record, ct);
LogBookRegistered(logger, created.DisplayName, created.Id, originalFileName);
filePath = null;
var suggestions = fivetoolsSourceKey is null
    ? registry.SuggestByName(displayName)
    : (IReadOnlyList<string>)Array.Empty<string>();
return Results.Accepted($"/admin/books/{created.Id}", new RegisterBookResponse(created, suggestions));
```

- [ ] **Step 6.4: Add `GET /admin/5etools/sources` to `FivetoolsAdminEndpoints.cs`**

```csharp
public static RouteGroupBuilder MapFivetoolsAdmin(this RouteGroupBuilder group)
{
    group.MapPost("/5etools/import",   ImportAll);
    group.MapGet ("/5etools/sources",  GetSources);
    return group;
}

private static IResult GetSources(BookSourceRegistry registry, string? group)
{
    var books = string.IsNullOrEmpty(group)
        ? registry.GetAll()
        : registry.GetAll()
            .Where(b => string.Equals(b.Group, group, StringComparison.OrdinalIgnoreCase))
            .ToList();
    return Results.Ok(books);
}
```

- [ ] **Step 6.5: Run failing tests again to confirm they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "RegisterBook_WithValidSourceKey|RegisterBook_WithUnknownSourceKey|RegisterBook_NoSourceKey_Response" -v minimal
```
Expected: all green.

- [ ] **Step 6.6: Run full test suite**

```bash
dotnet test DndMcpAICsharpFun.Tests -v minimal 2>&1 | tail -10
```

- [ ] **Step 6.7: Commit**

```bash
git add Features/Admin/BooksAdminEndpoints.cs \
        Features/Admin/FivetoolsAdminEndpoints.cs \
        DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs
git commit -m "feat(admin): fivetoolsSourceKey on registration + GET /admin/5etools/sources"
```

---

## Task 7: Extraction Source Key Propagation

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs`

- [ ] **Step 7.1: Write failing test for source key + edition propagation**

In `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs`, add:
```csharp
[Fact]
public async Task ExtractAsync_BookWithSourceKey_SetsSourceBookAndEditionFromRegistry()
{
    // Use the existing BuildOrchestrator helper but pass a record with FivetoolsSourceKey set.
    // Verify the written canonical JSON contains source="PHB" and edition="Edition2014".
    var record = new IngestionRecord
    {
        Id = 99, DisplayName = "Player's Handbook", Version = "Edition2014",
        FilePath = "<dummy>", FileName = "phb.pdf", FileHash = "",
        FivetoolsSourceKey = "PHB"
    };
    // ... (follow the same pattern as the existing test that builds an orchestrator and asserts on the written canonical JSON file)
    // After extraction, load the written canonical JSON and assert:
    // entities[0].SourceBook == "PHB"
    // entities[0].Edition    == "Edition2014"
}
```
(Look at the existing test `ExtractAsync_` tests in this file for the exact mock/setup pattern, and replicate it with a record that has `FivetoolsSourceKey = "PHB"`.)

- [ ] **Step 7.2: Inject `BookSourceRegistry` into `EntityExtractionOrchestrator`**

In `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`, find the constructor primary parameter list and add `BookSourceRegistry registry` (it is a singleton and already registered in DI from Task 1.5):
```csharp
public sealed class EntityExtractionOrchestrator(
    IIngestionTracker tracker,
    BookSourceRegistry registry,          // new
    // ... existing params ...
    ILogger<EntityExtractionOrchestrator> logger) : IEntityExtractionOrchestrator
```

- [ ] **Step 7.3: Use source key in `RunFullExtractionAsync` when building `EntityEnvelope`**

In `RunFullExtractionAsync`, just before the `var envelope = new EntityEnvelope(` line, compute the normalised values:
```csharp
var sourceBook = record.FivetoolsSourceKey ?? record.DisplayName;
var edition    = record.FivetoolsSourceKey is { } key && registry.TryGetBook(key) is { } info
    ? (info.PublishedYear >= 2024 ? "Edition2024" : "Edition2014")
    : record.Version;
```

Then replace `SourceBook: record.DisplayName` and `Edition: record.Version` in the `EntityEnvelope` constructor with:
```csharp
SourceBook:      sourceBook,
Edition:         edition,
...
FirstAppearedIn: new FirstAppearance(sourceBook, edition, candidate.Page),
```

- [ ] **Step 7.4: Do the same for the checkpoint-resume path** (`RunErrorsOnlyAsync` has a similar `new EntityEnvelope(` block — apply the same substitution there)

- [ ] **Step 7.5: Run tests**

```bash
dotnet test DndMcpAICsharpFun.Tests -v minimal 2>&1 | tail -10
```

- [ ] **Step 7.6: Commit**

```bash
git add Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs \
        DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs
git commit -m "feat(extraction): propagate FivetoolsSourceKey and derived edition into extracted entities"
```

---

## Task 8: Ingestion SourceBook Normalisation

**Files:**
- Modify: `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs`

- [ ] **Step 8.1: Write failing test**

In `DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs`, add:
```csharp
[Fact]
public async Task IngestEntitiesAsync_BookWithSourceKey_NormalisesSourceBookInQdrantPoint()
{
    // Arrange: build an orchestrator where the IngestionRecord has FivetoolsSourceKey = "PHB".
    // Provide a canonical JSON file with an entity that has SourceBook = "Player's Handbook (2014)".
    // After IngestEntitiesAsync runs, verify that the EntityPoint passed to IEntityVectorStore.UpsertAsync
    // has Envelope.SourceBook == "PHB".
    var store   = Substitute.For<IEntityVectorStore>();
    var tracker = Substitute.For<IIngestionTracker>();
    var record  = new IngestionRecord
    {
        Id = 1, DisplayName = "Player's Handbook", Version = "Edition2014",
        FilePath = "phb.pdf", FileName = "phb.pdf", FileHash = "abc",
        FivetoolsSourceKey = "PHB"
    };
    tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);

    // Write a minimal canonical JSON to a temp path
    var dir = Path.GetTempPath();
    var slug = "player-s-handbook";
    var canonicalPath = Path.Combine(dir, slug + ".json");
    await File.WriteAllTextAsync(canonicalPath, """
    {
      "schemaVersion": "1.0",
      "book": { "sourceBook": "Player's Handbook (2014)", "edition": "Edition2014", "fileHash": "abc", "displayName": "Player's Handbook" },
      "entities": [{
        "id": "phb.class.fighter", "type": "Class", "name": "Fighter",
        "sourceBook": "Player's Handbook (2014)", "edition": "Edition2014",
        "page": 70,
        "firstAppearedIn": { "book": "Player's Handbook (2014)", "edition": "Edition2014", "page": 70 },
        "revisedIn": [], "settingTags": [], "canonicalText": "", "fields": {}
      }]
    }
    """);

    IList<EntityPoint>? capturedPoints = null;
    await store.UpsertAsync(Arg.Do<IList<EntityPoint>>(pts => capturedPoints = pts), Arg.Any<CancellationToken>());

    // Act
    var orchestrator = BuildOrchestrator(tracker, store, canonicalDir: dir);
    await orchestrator.IngestEntitiesAsync(1);

    // Assert
    capturedPoints.Should().NotBeNull();
    capturedPoints![0].Envelope.SourceBook.Should().Be("PHB");
}
```
(Follow the pattern of existing orchestrator tests for `BuildOrchestrator`.)

- [ ] **Step 8.2: Normalise `SourceBook` in `EntityIngestionOrchestrator.IngestEntitiesAsync`**

In `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs`, inside the `foreach (var envelope in file.Entities)` loop, replace:
```csharp
renderedEnvelopes.Add(envelope with { CanonicalText = text, DataSource = ds });
```
with:
```csharp
var sourceBook = record.FivetoolsSourceKey ?? envelope.SourceBook;
renderedEnvelopes.Add(envelope with { CanonicalText = text, DataSource = ds, SourceBook = sourceBook });
```

- [ ] **Step 8.3: Run tests**

```bash
dotnet test DndMcpAICsharpFun.Tests -v minimal 2>&1 | tail -10
```

- [ ] **Step 8.4: Commit**

```bash
git add Features/Ingestion/Entities/EntityIngestionOrchestrator.cs \
        DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs
git commit -m "feat(ingestion): normalise Qdrant sourceBook to FivetoolsSourceKey during entity ingestion"
```

---

## Task 9: Entity Search SRD Filter Parameters

**Files:**
- Modify: `Features/Retrieval/Entities/EntitySearchQuery.cs`
- Modify: `Features/Retrieval/Entities/EntityRetrievalEndpoints.cs`
- Modify: `Features/Retrieval/Entities/EntityRetrievalService.cs`

- [ ] **Step 9.1: Add SRD params to `EntitySearchQuery`**

Replace the current record with:
```csharp
public sealed record EntitySearchQuery(
    string QueryText,
    EntityType? Type,
    string? SourceBook,
    string? Edition,
    string? BookType,
    string? SettingTag,
    string? Keyword,
    double? CrNumericLte,
    double? CrNumericGte,
    int? SpellLevel,
    string? DamageType,
    int TopK,
    bool? Srd = null,
    bool? Srd52 = null,
    bool? BasicRules2024 = null);
```

- [ ] **Step 9.2: Pass SRD flags through in `EntityRetrievalService.ExecuteAsync`**

In `Features/Retrieval/Entities/EntityRetrievalService.cs`, update the `new EntityFilters(` call:
```csharp
return await store.SearchAsync(vector, new EntityFilters(
    Type: q.Type, SourceBook: q.SourceBook, Edition: q.Edition,
    BookType: q.BookType, SettingTag: q.SettingTag, Keyword: q.Keyword,
    CrNumericLte: q.CrNumericLte, CrNumericGte: q.CrNumericGte,
    SpellLevel: q.SpellLevel, DamageType: q.DamageType,
    Srd: q.Srd, Srd52: q.Srd52, BasicRules2024: q.BasicRules2024
), topK, ct);
```

- [ ] **Step 9.3: Accept SRD query params in `EntityRetrievalEndpoints`**

In `Features/Retrieval/Entities/EntityRetrievalEndpoints.cs`, update `SearchPublic`, `SearchDiagnostic`, and `BuildQuery` to add `bool? srd, bool? srd52, bool? basicRules2024`:

```csharp
private static async Task<IResult> SearchPublic(
    string? q, string? type, string? sourceBook, string? edition, string? bookType,
    string? settingTag, string? keyword, double? crNumeric_lte, double? crNumeric_gte,
    int? spellLevel, string? damageType,
    bool? srd, bool? srd52, bool? basicRules2024,   // new
    int topK,
    IEntityRetrievalService svc, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest("Query parameter 'q' is required.");
    var results = await svc.SearchAsync(
        BuildQuery(q, type, sourceBook, edition, bookType, settingTag, keyword,
                   crNumeric_lte, crNumeric_gte, spellLevel, damageType,
                   srd, srd52, basicRules2024, topK), ct);
    return Results.Ok(results);
}

private static async Task<IResult> SearchDiagnostic(
    string? q, string? type, string? sourceBook, string? edition, string? bookType,
    string? settingTag, string? keyword, double? crNumeric_lte, double? crNumeric_gte,
    int? spellLevel, string? damageType,
    bool? srd, bool? srd52, bool? basicRules2024,   // new
    int topK,
    IEntityRetrievalService svc, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest("Query parameter 'q' is required.");
    var results = await svc.SearchDiagnosticAsync(
        BuildQuery(q, type, sourceBook, edition, bookType, settingTag, keyword,
                   crNumeric_lte, crNumeric_gte, spellLevel, damageType,
                   srd, srd52, basicRules2024, topK), ct);
    return Results.Ok(results);
}

private static EntitySearchQuery BuildQuery(
    string q, string? type, string? sourceBook, string? edition, string? bookType,
    string? settingTag, string? keyword, double? crLte, double? crGte,
    int? spellLevel, string? damageType,
    bool? srd, bool? srd52, bool? basicRules2024,   // new
    int topK)
{
    EntityType? parsedType = Enum.TryParse<EntityType>(type, ignoreCase: true, out var t) ? t : null;
    return new EntitySearchQuery(
        q, parsedType, sourceBook, edition, bookType, settingTag, keyword,
        crLte, crGte, spellLevel, damageType, topK <= 0 ? 10 : topK,
        Srd: srd, Srd52: srd52, BasicRules2024: basicRules2024);
}
```

- [ ] **Step 9.4: Build and run full test suite**

```bash
dotnet build -warnaserror 2>&1 | tail -5
dotnet test DndMcpAICsharpFun.Tests -v minimal 2>&1 | tail -10
```

- [ ] **Step 9.5: Commit**

```bash
git add Features/Retrieval/Entities/EntitySearchQuery.cs \
        Features/Retrieval/Entities/EntityRetrievalEndpoints.cs \
        Features/Retrieval/Entities/EntityRetrievalService.cs
git commit -m "feat(retrieval): expose srd/srd52/basicRules2024 filter params on entity search endpoints"
```

---

## Task 10: HTTP Contracts

**Files:**
- Modify: `DndMcpAICsharpFun.http`
- Modify: `dnd-mcp-api.insomnia.json`

- [ ] **Step 10.1: Add examples to `DndMcpAICsharpFun.http`**

Add the following sections to `DndMcpAICsharpFun.http`:

```http
### GET /admin/5etools/sources — full list
GET {{baseUrl}}/admin/5etools/sources
Accept: application/json

###

### GET /admin/5etools/sources?group=core — filtered
GET {{baseUrl}}/admin/5etools/sources?group=core
Accept: application/json

###

### POST /admin/books/register — with fivetoolsSourceKey
POST {{baseUrl}}/admin/books/register
Content-Type: multipart/form-data; boundary=----FormBoundary

------FormBoundary
Content-Disposition: form-data; name="version"

Edition2014
------FormBoundary
Content-Disposition: form-data; name="displayName"

Player's Handbook
------FormBoundary
Content-Disposition: form-data; name="bookType"

Core
------FormBoundary
Content-Disposition: form-data; name="fivetoolsSourceKey"

PHB
------FormBoundary
Content-Disposition: form-data; name="file"; filename="phb.pdf"
Content-Type: application/pdf

< ./books/phb.pdf
------FormBoundary--

###

### GET /retrieval/entities/search — filtered to SRD 5.2.1 only
GET {{baseUrl}}/retrieval/entities/search?q=fireball&srd52=true&topK=5
Accept: application/json

###

### GET /admin/retrieval/entities/search — SRD diagnostic
GET {{baseUrl}}/admin/retrieval/entities/search?q=fireball&srd52=true&topK=5
Accept: application/json
```

- [ ] **Step 10.2: Sync `dnd-mcp-api.insomnia.json`**

Add matching request entries in `dnd-mcp-api.insomnia.json` for:
- `GET /admin/5etools/sources`
- `GET /admin/5etools/sources?group=core`
- Updated `POST /admin/books/register` with `fivetoolsSourceKey` field
- `GET /retrieval/entities/search?q=fireball&srd52=true`
- `GET /admin/retrieval/entities/search?q=fireball&srd52=true`

(Follow the existing JSON structure in that file — each request is an object with `_id`, `_type: "request"`, `name`, `method`, `url`, `body`, and parent folder `parentId`.)

- [ ] **Step 10.3: Final full build + test**

```bash
dotnet build -warnaserror && dotnet test DndMcpAICsharpFun.Tests -v minimal
```

- [ ] **Step 10.4: Commit**

```bash
git add DndMcpAICsharpFun.http dnd-mcp-api.insomnia.json
git commit -m "docs(http): add GET /admin/5etools/sources + fivetoolsSourceKey + SRD filter examples"
```
