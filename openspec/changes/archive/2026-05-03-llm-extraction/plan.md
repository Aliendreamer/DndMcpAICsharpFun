# LLM Extraction Pipeline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace raw PDF text chunking with a two-pass LLM extraction pipeline: classify page content → extract typed entities → persist JSON → merge partial entities → embed → Qdrant.

**Architecture:** Stage 1 (extract): per-page Ollama classify + extract calls, results written to `{booksPath}/extracted/{bookId}/page_{n}.json`. Stage 2 (ingest-json): load page JSONs, run merge pass, embed each entity description, upsert to Qdrant. Two new admin endpoints trigger each stage; existing register/reingest flow is unchanged.

**Tech Stack:** OllamaSharp 5.4.25 (`OllamaApiClient.ChatAsync`), System.Text.Json, PdfPigTextExtractor (unchanged), xUnit + NSubstitute for tests.

---

## File Map

| Action | File |
|--------|------|
| Create | `Domain/ExtractedEntity.cs` |
| Create | `Features/Ingestion/Extraction/ILlmClassifier.cs` |
| Create | `Features/Ingestion/Extraction/OllamaLlmClassifier.cs` |
| Create | `Features/Ingestion/Extraction/ILlmEntityExtractor.cs` |
| Create | `Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs` |
| Create | `Features/Ingestion/Extraction/IEntityJsonStore.cs` |
| Create | `Features/Ingestion/Extraction/EntityJsonStore.cs` |
| Create | `Features/Ingestion/Extraction/IJsonIngestionPipeline.cs` |
| Create | `Features/Ingestion/Extraction/JsonIngestionPipeline.cs` |
| Create | `DndMcpAICsharpFun.Tests/Ingestion/Extraction/EntityJsonStoreTests.cs` |
| Create | `DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs` |
| Modify | `Infrastructure/Ollama/OllamaOptions.cs` |
| Modify | `Infrastructure/Sqlite/IngestionStatus.cs` |
| Modify | `Features/Ingestion/Tracking/IIngestionTracker.cs` |
| Modify | `Features/Ingestion/Tracking/SqliteIngestionTracker.cs` |
| Modify | `Features/Ingestion/IIngestionOrchestrator.cs` |
| Modify | `Features/Ingestion/IngestionOrchestrator.cs` |
| Modify | `Features/Admin/BooksAdminEndpoints.cs` |
| Modify | `Extensions/ServiceCollectionExtensions.cs` |
| Modify | `docker-compose.yml` |
| Modify | `Config/appsettings.json` |
| Modify | `DndMcpAICsharpFun.http` |

---

### Task 1: Foundation — OllamaOptions, IngestionStatus, ExtractedEntity

**Files:**
- Modify: `Infrastructure/Ollama/OllamaOptions.cs`
- Modify: `Infrastructure/Sqlite/IngestionStatus.cs`
- Create: `Domain/ExtractedEntity.cs`

- [ ] **Step 1: Add ExtractionModel to OllamaOptions**

Replace the full file content:

```csharp
namespace DndMcpAICsharpFun.Infrastructure.Ollama;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public string ExtractionModel { get; set; } = "llama3.2";
}
```

- [ ] **Step 2: Add Extracted and JsonIngested to IngestionStatus**

```csharp
namespace DndMcpAICsharpFun.Infrastructure.Sqlite;

public enum IngestionStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Duplicate,
    Extracted,
    JsonIngested,
}
```

No EF migration needed — enums are stored as integers; new values don't change the schema.

- [ ] **Step 3: Create Domain/ExtractedEntity.cs**

```csharp
using System.Text.Json.Nodes;

namespace DndMcpAICsharpFun.Domain;

public sealed record ExtractedEntity(
    int Page,
    string SourceBook,
    string Version,
    bool Partial,
    string Type,
    string Name,
    JsonObject Data);
```

`Data` is `JsonObject` (from `System.Text.Json.Nodes`) so it preserves the raw JSON fields without a fixed schema, while still being serializable.

- [ ] **Step 4: Build and verify no errors**

```bash
dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Infrastructure/Ollama/OllamaOptions.cs \
        Infrastructure/Sqlite/IngestionStatus.cs \
        Domain/ExtractedEntity.cs
git commit -m "feat: add ExtractionModel to OllamaOptions, Extracted/JsonIngested status, ExtractedEntity domain model"
```

---

### Task 2: ILlmClassifier + OllamaLlmClassifier (Pass 1)

**Files:**
- Create: `Features/Ingestion/Extraction/ILlmClassifier.cs`
- Create: `Features/Ingestion/Extraction/OllamaLlmClassifier.cs`

- [ ] **Step 1: Create the interface**

```csharp
// Features/Ingestion/Extraction/ILlmClassifier.cs
namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface ILlmClassifier
{
    Task<IReadOnlyList<string>> ClassifyPageAsync(string pageText, CancellationToken ct = default);
}
```

Returns the detected entity type names (e.g., `["Spell", "Rule"]`). Returns empty list for sparse pages or when the LLM returns `[]`. Log warning and return empty on invalid JSON.

- [ ] **Step 2: Create OllamaLlmClassifier**

```csharp
// Features/Ingestion/Extraction/OllamaLlmClassifier.cs
using System.Text.Json;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed partial class OllamaLlmClassifier(
    OllamaApiClient ollama,
    IOptions<OllamaOptions> options,
    ILogger<OllamaLlmClassifier> logger) : ILlmClassifier
{
    private const string SystemPrompt =
        """
        You are a D&D 5e content classifier. Given a page of text from a D&D rulebook,
        list only the entity types present on this page. Reply with a JSON array of strings
        using only these values: Spell, Monster, Class, Background, Item, Rule, Treasure,
        Encounter, Trap. Reply with [] if no entities are found. Reply with JSON only, no
        explanation.
        """;

    private readonly string _model = options.Value.ExtractionModel;

    public async Task<IReadOnlyList<string>> ClassifyPageAsync(string pageText, CancellationToken ct = default)
    {
        var request = new ChatRequest
        {
            Model = _model,
            Stream = false,
            Messages =
            [
                new Message { Role = ChatRole.System, Content = SystemPrompt },
                new Message { Role = ChatRole.User, Content = pageText }
            ]
        };

        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in ollama.ChatAsync(request, ct))
            sb.Append(chunk?.Message?.Content ?? string.Empty);

        var json = sb.ToString().Trim();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            LogInvalidJson(logger, json[..Math.Min(200, json.Length)]);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Classifier returned invalid JSON: {Json}")]
    private static partial void LogInvalidJson(ILogger logger, string json);
}
```

- [ ] **Step 3: Build and verify**

```bash
dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Features/Ingestion/Extraction/ILlmClassifier.cs \
        Features/Ingestion/Extraction/OllamaLlmClassifier.cs
git commit -m "feat: add ILlmClassifier + OllamaLlmClassifier (Pass 1 page classification)"
```

---

### Task 3: ILlmEntityExtractor + OllamaLlmEntityExtractor (Pass 2)

**Files:**
- Create: `Features/Ingestion/Extraction/ILlmEntityExtractor.cs`
- Create: `Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs`

- [ ] **Step 1: Create the interface**

```csharp
// Features/Ingestion/Extraction/ILlmEntityExtractor.cs
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface ILlmEntityExtractor
{
    Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(
        string pageText,
        string entityType,
        int pageNumber,
        string sourceBook,
        string version,
        CancellationToken ct = default);
}
```

One call per (page, type). The extractor fills `Page`, `SourceBook`, `Version`, `Type` from the known context; LLM only returns `name`, `partial`, `data`.

- [ ] **Step 2: Create OllamaLlmEntityExtractor**

```csharp
// Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed partial class OllamaLlmEntityExtractor(
    OllamaApiClient ollama,
    IOptions<OllamaOptions> options,
    ILogger<OllamaLlmEntityExtractor> logger) : ILlmEntityExtractor
{
    private static readonly Dictionary<string, string> TypeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Spell"]      = "level (int), school (string), casting_time (string), range (string), components (string), duration (string), description (string)",
        ["Monster"]    = "size (string), type (string), alignment (string), ac (int), hp (string), speed (string), abilities (object: str/dex/con/int/wis/cha), description (string)",
        ["Class"]      = "hit_die (string), primary_ability (string), saving_throws (string[]), armor_proficiencies (string), weapon_proficiencies (string), features (array of {level, name, description})",
        ["Background"] = "description (string)",
        ["Item"]       = "description (string)",
        ["Rule"]       = "description (string)",
        ["Treasure"]   = "description (string)",
        ["Encounter"]  = "description (string)",
        ["Trap"]       = "description (string)",
    };

    private readonly string _model = options.Value.ExtractionModel;

    public async Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(
        string pageText,
        string entityType,
        int pageNumber,
        string sourceBook,
        string version,
        CancellationToken ct = default)
    {
        var fields = TypeFields.GetValueOrDefault(entityType, "description (string)");
        var systemPrompt =
            $"""
            You are a D&D 5e content extractor. Extract all {entityType} entities from the page text
            below. Return a JSON array of objects. Each object must have:
            - name (string)
            - partial (bool — true if the entity appears cut off at the page boundary)
            - data (object with the fields listed below)

            Use null for any missing fields. Reply with JSON only, no explanation.

            Fields for {entityType}: {fields}
            """;

        var request = new ChatRequest
        {
            Model = _model,
            Stream = false,
            Messages =
            [
                new Message { Role = ChatRole.System, Content = systemPrompt },
                new Message { Role = ChatRole.User, Content = pageText }
            ]
        };

        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in ollama.ChatAsync(request, ct))
            sb.Append(chunk?.Message?.Content ?? string.Empty);

        var json = sb.ToString().Trim();

        try
        {
            var raw = JsonNode.Parse(json)?.AsArray();
            if (raw is null) return [];

            var results = new List<ExtractedEntity>();
            foreach (var item in raw)
            {
                if (item is not JsonObject obj) continue;
                var name = obj["name"]?.GetValue<string>() ?? string.Empty;
                var partial = obj["partial"]?.GetValue<bool>() ?? false;
                var data = obj["data"]?.AsObject() ?? new JsonObject();

                results.Add(new ExtractedEntity(pageNumber, sourceBook, version, partial, entityType, name, data));
            }
            return results;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            LogInvalidJson(logger, entityType, pageNumber, json[..Math.Min(200, json.Length)]);

            // Fallback: save raw page text as a Rule entity with partial:true — nothing silently dropped
            var fallbackData = new JsonObject { ["description"] = pageText };
            return [new ExtractedEntity(pageNumber, sourceBook, version, true, "Rule", $"page_{pageNumber}_raw", fallbackData)];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Extractor returned invalid JSON for type={Type} page={Page}: {Json}")]
    private static partial void LogInvalidJson(ILogger logger, string type, int page, string json);
}
```

- [ ] **Step 3: Build and verify**

```bash
dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Features/Ingestion/Extraction/ILlmEntityExtractor.cs \
        Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs
git commit -m "feat: add ILlmEntityExtractor + OllamaLlmEntityExtractor (Pass 2 typed entity extraction)"
```

---

### Task 4: IEntityJsonStore + EntityJsonStore

**Files:**
- Create: `Features/Ingestion/Extraction/IEntityJsonStore.cs`
- Create: `Features/Ingestion/Extraction/EntityJsonStore.cs`
- Create: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/EntityJsonStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// DndMcpAICsharpFun.Tests/Ingestion/Extraction/EntityJsonStoreTests.cs
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public sealed class EntityJsonStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly EntityJsonStore _store;

    public EntityJsonStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var opts = Options.Create(new IngestionOptions { BooksPath = _tempRoot });
        _store = new EntityJsonStore(opts);
    }

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    [Fact]
    public async Task SaveAndLoadRoundtrip()
    {
        var entity = new ExtractedEntity(1, "PHB", "Edition2014", false, "Spell", "Fireball",
            new JsonObject { ["level"] = 3, ["description"] = "Big fire." });

        await _store.SavePageAsync(bookId: 99, pageNumber: 1, [entity]);

        var pages = await _store.LoadAllPagesAsync(bookId: 99);

        Assert.Single(pages);
        Assert.Single(pages[0]);
        var loaded = pages[0][0];
        Assert.Equal("Fireball", loaded.Name);
        Assert.Equal(3, loaded.Data["level"]?.GetValue<int>());
    }

    [Fact]
    public async Task LoadAllPagesReturnsInPageOrder()
    {
        await _store.SavePageAsync(99, 3, [MakeEntity("C", 3)]);
        await _store.SavePageAsync(99, 1, [MakeEntity("A", 1)]);
        await _store.SavePageAsync(99, 2, [MakeEntity("B", 2)]);

        var pages = await _store.LoadAllPagesAsync(99);

        Assert.Equal(3, pages.Count);
        Assert.Equal("A", pages[0][0].Name);
        Assert.Equal("B", pages[1][0].Name);
        Assert.Equal("C", pages[2][0].Name);
    }

    [Fact]
    public async Task MergePassConcatenatesDescriptionAndDropsDuplicate()
    {
        var partialEntity = new ExtractedEntity(1, "PHB", "Edition2014", true, "Spell", "Fireball",
            new JsonObject { ["description"] = "Big fire" });
        var continuedEntity = new ExtractedEntity(2, "PHB", "Edition2014", false, "Spell", "Fireball",
            new JsonObject { ["description"] = " ball." });

        await _store.SavePageAsync(99, 1, [partialEntity]);
        await _store.SavePageAsync(99, 2, [continuedEntity]);

        await _store.RunMergePassAsync(99);

        var pages = await _store.LoadAllPagesAsync(99);
        // page 1 entity merged; page 2 entity removed
        Assert.Single(pages[0]);
        Assert.Empty(pages[1]);
        Assert.Equal("Big fire ball.", pages[0][0].Data["description"]?.GetValue<string>());
        Assert.False(pages[0][0].Partial);
    }

    [Fact]
    public void ListPageFilesReturnsAllFiles()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "extracted", "99"));
        File.WriteAllText(Path.Combine(_tempRoot, "extracted", "99", "page_1.json"), "[]");
        File.WriteAllText(Path.Combine(_tempRoot, "extracted", "99", "page_2.json"), "[]");

        var files = _store.ListPageFiles(99).ToList();
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void DeleteAllPagesRemovesDirectory()
    {
        var dir = Path.Combine(_tempRoot, "extracted", "99");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "page_1.json"), "[]");

        _store.DeleteAllPages(99);

        Assert.False(Directory.Exists(dir));
    }

    private static ExtractedEntity MakeEntity(string name, int page) =>
        new(page, "PHB", "Edition2014", false, "Rule", name, new JsonObject { ["description"] = name });
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "EntityJsonStoreTests" --no-build 2>&1 | tail -20
```

Expected: FAIL — EntityJsonStore not found.

- [ ] **Step 3: Create IEntityJsonStore**

```csharp
// Features/Ingestion/Extraction/IEntityJsonStore.cs
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface IEntityJsonStore
{
    Task SavePageAsync(int bookId, int pageNumber, IReadOnlyList<ExtractedEntity> entities, CancellationToken ct = default);
    Task<IReadOnlyList<IReadOnlyList<ExtractedEntity>>> LoadAllPagesAsync(int bookId, CancellationToken ct = default);
    Task RunMergePassAsync(int bookId, CancellationToken ct = default);
    IEnumerable<string> ListPageFiles(int bookId);
    void DeleteAllPages(int bookId);
}
```

- [ ] **Step 4: Create EntityJsonStore**

```csharp
// Features/Ingestion/Extraction/EntityJsonStore.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed class EntityJsonStore(IOptions<IngestionOptions> options) : IEntityJsonStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private string ExtractedDir(int bookId) =>
        Path.Combine(options.Value.BooksPath, "extracted", bookId.ToString());

    private static string PageFile(string dir, int pageNumber) =>
        Path.Combine(dir, $"page_{pageNumber}.json");

    public async Task SavePageAsync(int bookId, int pageNumber, IReadOnlyList<ExtractedEntity> entities, CancellationToken ct = default)
    {
        var dir = ExtractedDir(bookId);
        Directory.CreateDirectory(dir);

        var array = new JsonArray();
        foreach (var e in entities)
        {
            array.Add(new JsonObject
            {
                ["page"]         = e.Page,
                ["source_book"]  = e.SourceBook,
                ["version"]      = e.Version,
                ["partial"]      = e.Partial,
                ["type"]         = e.Type,
                ["name"]         = e.Name,
                ["data"]         = JsonNode.Parse(e.Data.ToJsonString())
            });
        }

        var path = PageFile(dir, pageNumber);
        await File.WriteAllTextAsync(path, array.ToJsonString(JsonOpts), ct);
    }

    public async Task<IReadOnlyList<IReadOnlyList<ExtractedEntity>>> LoadAllPagesAsync(int bookId, CancellationToken ct = default)
    {
        var dir = ExtractedDir(bookId);
        if (!Directory.Exists(dir))
            return [];

        var files = Directory.GetFiles(dir, "page_*.json")
            .OrderBy(ExtractPageNumber)
            .ToList();

        var result = new List<IReadOnlyList<ExtractedEntity>>();
        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file, ct);
            result.Add(ParsePageFile(json));
        }
        return result;
    }

    public async Task RunMergePassAsync(int bookId, CancellationToken ct = default)
    {
        var dir = ExtractedDir(bookId);
        if (!Directory.Exists(dir)) return;

        var files = Directory.GetFiles(dir, "page_*.json")
            .OrderBy(ExtractPageNumber)
            .ToList();

        if (files.Count < 2) return;

        // Load all pages as mutable lists
        var pages = new List<List<ExtractedEntity>>();
        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file, ct);
            pages.Add([.. ParsePageFile(json)]);
        }

        // Merge: if page[i] has entity X with partial=true and page[i+1] has same type+name, concatenate
        for (int i = 0; i < pages.Count - 1; i++)
        {
            var current = pages[i];
            var next = pages[i + 1];

            foreach (var entity in current.Where(e => e.Partial))
            {
                var match = next.FirstOrDefault(e =>
                    string.Equals(e.Type, entity.Type, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Name, entity.Name, StringComparison.OrdinalIgnoreCase));

                if (match is null) continue;

                var thisDesc = entity.Data["description"]?.GetValue<string>() ?? string.Empty;
                var nextDesc = match.Data["description"]?.GetValue<string>() ?? string.Empty;
                var mergedData = JsonNode.Parse(entity.Data.ToJsonString())!.AsObject();
                mergedData["description"] = thisDesc + nextDesc;

                var merged = entity with { Partial = false, Data = mergedData };
                current[current.IndexOf(entity)] = merged;
                next.Remove(match);
            }
        }

        // Persist updated pages back to disk
        for (int i = 0; i < files.Count; i++)
        {
            var pageNumber = ExtractPageNumber(files[i]);
            // Build fresh list so SavePageAsync doesn't conflict with an in-flight read
            await SavePageAsync(bookId, pageNumber, pages[i], ct);
        }
    }

    public IEnumerable<string> ListPageFiles(int bookId)
    {
        var dir = ExtractedDir(bookId);
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "page_*.json").OrderBy(ExtractPageNumber)
            : [];
    }

    public void DeleteAllPages(int bookId)
    {
        var dir = ExtractedDir(bookId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private static IReadOnlyList<ExtractedEntity> ParsePageFile(string json)
    {
        var array = JsonNode.Parse(json)?.AsArray();
        if (array is null) return [];

        var result = new List<ExtractedEntity>();
        foreach (var node in array)
        {
            if (node is not JsonObject obj) continue;
            var data = obj["data"]?.AsObject() ?? new JsonObject();
            result.Add(new ExtractedEntity(
                Page:       obj["page"]?.GetValue<int>() ?? 0,
                SourceBook: obj["source_book"]?.GetValue<string>() ?? string.Empty,
                Version:    obj["version"]?.GetValue<string>() ?? string.Empty,
                Partial:    obj["partial"]?.GetValue<bool>() ?? false,
                Type:       obj["type"]?.GetValue<string>() ?? string.Empty,
                Name:       obj["name"]?.GetValue<string>() ?? string.Empty,
                Data:       data));
        }
        return result;
    }

    private static int ExtractPageNumber(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath); // "page_42"
        return int.TryParse(name.AsSpan(5), out var n) ? n : 0;
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "EntityJsonStoreTests" -v normal 2>&1 | tail -30
```

Expected: All 5 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add Features/Ingestion/Extraction/IEntityJsonStore.cs \
        Features/Ingestion/Extraction/EntityJsonStore.cs \
        DndMcpAICsharpFun.Tests/Ingestion/Extraction/EntityJsonStoreTests.cs
git commit -m "feat: add IEntityJsonStore + EntityJsonStore with merge pass"
```

---

### Task 5: IJsonIngestionPipeline + JsonIngestionPipeline (Stage 2)

**Files:**
- Create: `Features/Ingestion/Extraction/IJsonIngestionPipeline.cs`
- Create: `Features/Ingestion/Extraction/JsonIngestionPipeline.cs`
- Create: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.VectorStore;
using NSubstitute;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public sealed class JsonIngestionPipelineTests
{
    private readonly IEntityJsonStore _store = Substitute.For<IEntityJsonStore>();
    private readonly IEmbeddingIngestor _ingestor = Substitute.For<IEmbeddingIngestor>();
    private readonly JsonIngestionPipeline _pipeline;

    public JsonIngestionPipelineTests()
    {
        _pipeline = new JsonIngestionPipeline(_store, _ingestor);
    }

    [Fact]
    public async Task IngestAsync_CallsMergePassThenEmbeds()
    {
        var entity = new ExtractedEntity(1, "PHB", "Edition2014", false, "Spell", "Fireball",
            new JsonObject { ["level"] = 3, ["description"] = "Big fire ball." });

        _store.LoadAllPagesAsync(42).Returns(
            Task.FromResult<IReadOnlyList<IReadOnlyList<ExtractedEntity>>>(
                [[entity]]));

        await _pipeline.IngestAsync(bookId: 42, fileHash: "abc123");

        await _store.Received(1).RunMergePassAsync(42, Arg.Any<CancellationToken>());
        await _ingestor.Received(1).IngestAsync(
            Arg.Is<IList<ContentChunk>>(chunks =>
                chunks.Count == 1 &&
                chunks[0].Text == "Big fire ball." &&
                chunks[0].Metadata.Category == ContentCategory.Spell &&
                chunks[0].Metadata.EntityName == "Fireball"),
            "abc123",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_SkipsEntitiesWithEmptyDescription()
    {
        var entity = new ExtractedEntity(1, "PHB", "Edition2014", false, "Rule", "Empty",
            new JsonObject { ["description"] = "   " });

        _store.LoadAllPagesAsync(42).Returns(
            Task.FromResult<IReadOnlyList<IReadOnlyList<ExtractedEntity>>>(
                [[entity]]));

        await _pipeline.IngestAsync(bookId: 42, fileHash: "abc123");

        await _ingestor.Received(1).IngestAsync(
            Arg.Is<IList<ContentChunk>>(chunks => chunks.Count == 0),
            "abc123",
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "JsonIngestionPipelineTests" 2>&1 | tail -10
```

Expected: FAIL — JsonIngestionPipeline not found.

- [ ] **Step 3: Create IJsonIngestionPipeline**

```csharp
// Features/Ingestion/Extraction/IJsonIngestionPipeline.cs
namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface IJsonIngestionPipeline
{
    Task IngestAsync(int bookId, string fileHash, CancellationToken ct = default);
}
```

- [ ] **Step 4: Create JsonIngestionPipeline**

```csharp
// Features/Ingestion/Extraction/JsonIngestionPipeline.cs
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.VectorStore;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed class JsonIngestionPipeline(
    IEntityJsonStore jsonStore,
    IEmbeddingIngestor embeddingIngestor) : IJsonIngestionPipeline
{
    public async Task IngestAsync(int bookId, string fileHash, CancellationToken ct = default)
    {
        await jsonStore.RunMergePassAsync(bookId, ct);

        var pages = await jsonStore.LoadAllPagesAsync(bookId, ct);

        var chunks = new List<ContentChunk>();
        int chunkIndex = 0;
        foreach (var page in pages)
        {
            foreach (var entity in page)
            {
                var description = entity.Data["description"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(description)) continue;

                if (!Enum.TryParse<ContentCategory>(entity.Type, ignoreCase: true, out var category))
                    category = ContentCategory.Rule;

                if (!Enum.TryParse<DndVersion>(entity.Version, ignoreCase: true, out var version))
                    version = DndVersion.Edition2014;

                var metadata = new ChunkMetadata(
                    SourceBook:  entity.SourceBook,
                    Version:     version,
                    Category:    category,
                    EntityName:  entity.Name,
                    Chapter:     string.Empty,
                    PageNumber:  entity.Page,
                    ChunkIndex:  chunkIndex++);

                chunks.Add(new ContentChunk(description, metadata));
            }
        }

        await embeddingIngestor.IngestAsync(chunks, fileHash, ct);
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "JsonIngestionPipelineTests" -v normal 2>&1 | tail -20
```

Expected: 2 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add Features/Ingestion/Extraction/IJsonIngestionPipeline.cs \
        Features/Ingestion/Extraction/JsonIngestionPipeline.cs \
        DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs
git commit -m "feat: add IJsonIngestionPipeline + JsonIngestionPipeline (Stage 2 embed + upsert)"
```

---

### Task 6: Tracker extension — MarkExtractedAsync + MarkJsonIngestedAsync

**Files:**
- Modify: `Features/Ingestion/Tracking/IIngestionTracker.cs`
- Modify: `Features/Ingestion/Tracking/SqliteIngestionTracker.cs`

- [ ] **Step 1: Add methods to IIngestionTracker**

Add two new method signatures at the end of the interface:

```csharp
// Features/Ingestion/Tracking/IIngestionTracker.cs
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Features.Ingestion.Tracking;

public interface IIngestionTracker
{
    Task<IngestionRecord?> GetByHashAsync(string hash, CancellationToken ct = default);
    Task<IngestionRecord> CreateAsync(IngestionRecord record, CancellationToken ct = default);
    Task MarkHashAsync(int id, string fileHash, CancellationToken ct = default);
    Task MarkCompletedAsync(int id, int chunkCount, CancellationToken ct = default);
    Task MarkFailedAsync(int id, string error, CancellationToken ct = default);
    Task MarkExtractedAsync(int id, CancellationToken ct = default);
    Task MarkJsonIngestedAsync(int id, int chunkCount, CancellationToken ct = default);
    Task ResetForReingestionAsync(int id, CancellationToken ct = default);
    Task<IList<IngestionRecord>> GetPendingAndFailedAsync(CancellationToken ct = default);
    Task<IList<IngestionRecord>> GetAllAsync(CancellationToken ct = default);
    Task<IngestionRecord?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IngestionRecord?> GetCompletedByHashAsync(string hash, int excludeId, CancellationToken ct = default);
    Task MarkDuplicateAsync(int id, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement in SqliteIngestionTracker**

Add at the end of the class (before the closing `}`):

```csharp
public async Task MarkExtractedAsync(int id, CancellationToken ct = default)
{
    await db.IngestionRecords
        .Where(r => r.Id == id)
        .ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, IngestionStatus.Extracted)
            .SetProperty(r => r.Error, (string?)null), ct);
}

public async Task MarkJsonIngestedAsync(int id, int chunkCount, CancellationToken ct = default)
{
    await db.IngestionRecords
        .Where(r => r.Id == id)
        .ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, IngestionStatus.JsonIngested)
            .SetProperty(r => r.ChunkCount, chunkCount)
            .SetProperty(r => r.IngestedAt, DateTime.UtcNow)
            .SetProperty(r => r.Error, (string?)null), ct);
}
```

- [ ] **Step 3: Build and verify**

```bash
dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Features/Ingestion/Tracking/IIngestionTracker.cs \
        Features/Ingestion/Tracking/SqliteIngestionTracker.cs
git commit -m "feat: add MarkExtractedAsync and MarkJsonIngestedAsync to tracker"
```

---

### Task 7: IIngestionOrchestrator extension + Stage 1 implementation

**Files:**
- Modify: `Features/Ingestion/IIngestionOrchestrator.cs`
- Modify: `Features/Ingestion/IngestionOrchestrator.cs`

- [ ] **Step 1: Add new methods to IIngestionOrchestrator**

```csharp
// Features/Ingestion/IIngestionOrchestrator.cs
namespace DndMcpAICsharpFun.Features.Ingestion;

public interface IIngestionOrchestrator
{
    Task IngestBookAsync(int recordId, CancellationToken cancellationToken = default);
    Task ExtractBookAsync(int recordId, CancellationToken cancellationToken = default);
    Task IngestJsonAsync(int recordId, CancellationToken cancellationToken = default);
    Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Add using + constructor params to IngestionOrchestrator**

Update the class header to inject new dependencies:

```csharp
// Features/Ingestion/IngestionOrchestrator.cs  — top of file
using System.Security.Cryptography;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion.Chunking;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed partial class IngestionOrchestrator(
    IIngestionTracker tracker,
    IPdfTextExtractor extractor,
    DndChunker chunker,
    IEmbeddingIngestor embeddingIngestor,
    IVectorStoreService vectorStore,
    ILlmClassifier classifier,
    ILlmEntityExtractor entityExtractor,
    IEntityJsonStore jsonStore,
    IJsonIngestionPipeline jsonPipeline,
    ILogger<IngestionOrchestrator> logger) : IIngestionOrchestrator
```

- [ ] **Step 3: Implement ExtractBookAsync (Stage 1)**

Add method after `IngestBookAsync`:

```csharp
public async Task ExtractBookAsync(int recordId, CancellationToken cancellationToken = default)
{
    var record = await tracker.GetByIdAsync(recordId, cancellationToken);
    if (record is null)
    {
        LogRecordNotFound(logger, recordId);
        return;
    }

    LogStartingExtraction(logger, record.DisplayName, recordId);

    try
    {
        await tracker.MarkHashAsync(recordId, record.FileHash, cancellationToken);

        var pages = extractor.ExtractPages(record.FilePath).ToList();
        var version = Enum.Parse<DndVersion>(record.Version);

        foreach (var (pageNumber, pageText) in pages)
        {
            if (pageText.Length < 100)
                continue;

            var types = await classifier.ClassifyPageAsync(pageText, cancellationToken);
            var pageEntities = new List<ExtractedEntity>();

            foreach (var type in types)
            {
                var extracted = await entityExtractor.ExtractAsync(
                    pageText, type, pageNumber, record.DisplayName, record.Version, cancellationToken);
                pageEntities.AddRange(extracted);
            }

            if (pageEntities.Count > 0)
                await jsonStore.SavePageAsync(recordId, pageNumber, pageEntities, cancellationToken);
        }

        await tracker.MarkExtractedAsync(recordId, cancellationToken);
        LogExtractedBook(logger, record.DisplayName, recordId);
    }
    catch (Exception ex)
    {
        LogExtractionFailed(logger, ex, record.DisplayName, recordId);
        await tracker.MarkFailedAsync(recordId, ex.Message, cancellationToken);
    }
}
```

- [ ] **Step 4: Implement IngestJsonAsync (Stage 2 delegate)**

Add method after `ExtractBookAsync`:

```csharp
public async Task IngestJsonAsync(int recordId, CancellationToken cancellationToken = default)
{
    var record = await tracker.GetByIdAsync(recordId, cancellationToken);
    if (record is null)
    {
        LogRecordNotFound(logger, recordId);
        return;
    }

    LogStartingJsonIngestion(logger, record.DisplayName, recordId);

    try
    {
        await tracker.MarkHashAsync(recordId, record.FileHash, cancellationToken);
        await jsonPipeline.IngestAsync(recordId, record.FileHash, cancellationToken);

        // Count chunks that landed in Qdrant (approximated from JSON store)
        var pages = await jsonStore.LoadAllPagesAsync(recordId, cancellationToken);
        var chunkCount = pages.Sum(p => p.Count);

        await tracker.MarkJsonIngestedAsync(recordId, chunkCount, cancellationToken);
        LogJsonIngested(logger, record.DisplayName, recordId, chunkCount);
    }
    catch (Exception ex)
    {
        LogJsonIngestionFailed(logger, ex, record.DisplayName, recordId);
        await tracker.MarkFailedAsync(recordId, ex.Message, cancellationToken);
    }
}
```

- [ ] **Step 5: Update DeleteBookAsync to also clean JSON files**

Replace the `DeleteBookAsync` method body with:

```csharp
public async Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken cancellationToken = default)
{
    var record = await tracker.GetByIdAsync(id, cancellationToken);
    if (record is null)
        return DeleteBookResult.NotFound;

    if (record.Status == IngestionStatus.Processing)
        return DeleteBookResult.Conflict;

    if (record.Status == IngestionStatus.Completed && record.ChunkCount.HasValue)
        await vectorStore.DeleteByHashAsync(record.FileHash, record.ChunkCount.Value, cancellationToken);

    jsonStore.DeleteAllPages(id);

    if (File.Exists(record.FilePath))
        File.Delete(record.FilePath);

    await tracker.DeleteAsync(id, cancellationToken);
    LogBookDeleted(logger, record.DisplayName, id);
    return DeleteBookResult.Deleted;
}
```

- [ ] **Step 6: Add log messages at the end of the class**

Add after the existing log messages:

```csharp
[LoggerMessage(Level = LogLevel.Information, Message = "Starting extraction for {DisplayName} (id={Id})")]
private static partial void LogStartingExtraction(ILogger logger, string displayName, int id);

[LoggerMessage(Level = LogLevel.Information, Message = "Extracted {DisplayName} (id={Id}) — JSON files saved")]
private static partial void LogExtractedBook(ILogger logger, string displayName, int id);

[LoggerMessage(Level = LogLevel.Error, Message = "Extraction failed for {DisplayName} (id={Id})")]
private static partial void LogExtractionFailed(ILogger logger, Exception ex, string displayName, int id);

[LoggerMessage(Level = LogLevel.Information, Message = "Starting JSON ingestion for {DisplayName} (id={Id})")]
private static partial void LogStartingJsonIngestion(ILogger logger, string displayName, int id);

[LoggerMessage(Level = LogLevel.Information, Message = "JSON ingestion completed for {DisplayName} (id={Id}): {Count} chunks")]
private static partial void LogJsonIngested(ILogger logger, string displayName, int id, int count);

[LoggerMessage(Level = LogLevel.Error, Message = "JSON ingestion failed for {DisplayName} (id={Id})")]
private static partial void LogJsonIngestionFailed(ILogger logger, Exception ex, string displayName, int id);
```

- [ ] **Step 7: Build and verify**

```bash
dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add Features/Ingestion/IIngestionOrchestrator.cs \
        Features/Ingestion/IngestionOrchestrator.cs
git commit -m "feat: implement ExtractBookAsync and IngestJsonAsync on IngestionOrchestrator"
```

---

### Task 8: New Admin Endpoints

**Files:**
- Modify: `Features/Admin/BooksAdminEndpoints.cs`
- Modify: `DndMcpAICsharpFun.http`

- [ ] **Step 1: Register the three new routes in MapBooksAdmin**

Replace the `MapBooksAdmin` method body with:

```csharp
public static RouteGroupBuilder MapBooksAdmin(this RouteGroupBuilder group)
{
    group.MapPost("/books/register", RegisterBook).DisableAntiforgery();
    group.MapPost("/books/register-path", RegisterBookByPath);
    group.MapGet("/books", GetAllBooks);
    group.MapPost("/books/{id:int}/reingest", ReingestBook);
    group.MapPost("/books/{id:int}/extract", ExtractBook);
    group.MapGet("/books/{id:int}/extracted", GetExtracted);
    group.MapPost("/books/{id:int}/ingest-json", IngestJson);
    group.MapDelete("/books/{id:int}", DeleteBook);
    return group;
}
```

- [ ] **Step 2: Add the three handler methods**

Add after the existing `ReingestBook` handler:

```csharp
private static async Task<IResult> ExtractBook(
    int id,
    IIngestionTracker tracker,
    IServiceScopeFactory scopeFactory,
    CancellationToken ct)
{
    var record = await tracker.GetByIdAsync(id, ct);
    if (record is null)
        return Results.NotFound($"Book with id {id} not found");

    if (record.Status == IngestionStatus.Processing)
        return Results.Conflict("Book is currently processing. Wait before re-extracting.");

    _ = Task.Run(async () =>
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IIngestionOrchestrator>();
        await orchestrator.ExtractBookAsync(id, CancellationToken.None);
    }, CancellationToken.None);

    return Results.Accepted($"/admin/books/{id}");
}

private static async Task<IResult> GetExtracted(
    int id,
    IIngestionTracker tracker,
    IEntityJsonStore jsonStore,
    CancellationToken ct)
{
    var record = await tracker.GetByIdAsync(id, ct);
    if (record is null)
        return Results.NotFound($"Book with id {id} not found");

    var files = jsonStore.ListPageFiles(id).ToList();
    return Results.Ok(new { BookId = id, FileCount = files.Count, Files = files });
}

private static async Task<IResult> IngestJson(
    int id,
    IIngestionTracker tracker,
    IServiceScopeFactory scopeFactory,
    CancellationToken ct)
{
    var record = await tracker.GetByIdAsync(id, ct);
    if (record is null)
        return Results.NotFound($"Book with id {id} not found");

    if (record.Status == IngestionStatus.Processing)
        return Results.Conflict("Book is currently processing.");

    _ = Task.Run(async () =>
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IIngestionOrchestrator>();
        await orchestrator.IngestJsonAsync(id, CancellationToken.None);
    }, CancellationToken.None);

    return Results.Accepted($"/admin/books/{id}");
}
```

Note: `GetExtracted` uses `IEntityJsonStore` which is scoped via DI — add the parameter as shown; ASP.NET Core minimal APIs resolve handler parameters from DI automatically.

- [ ] **Step 3: Add missing using for IngestionStatus**

The new handlers reference `IngestionStatus.Processing`. `BooksAdminEndpoints.cs` already has `using DndMcpAICsharpFun.Infrastructure.Sqlite;` — no new using needed. But also add using for `IEntityJsonStore`:

At the top of `BooksAdminEndpoints.cs`, add:
```csharp
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
```

- [ ] **Step 4: Update DndMcpAICsharpFun.http**

Add these three request examples after the existing reingest section (before the Qdrant section):

```
### Admin Books — Extract book with LLM (fires Stage 1 in background)
POST {{baseUrl}}/admin/books/1/extract
X-Admin-Api-Key: {{adminKey}}

### Admin Books — List extracted JSON files for a book
GET {{baseUrl}}/admin/books/1/extracted
X-Admin-Api-Key: {{adminKey}}

### Admin Books — Ingest from extracted JSON (fires Stage 2 in background)
POST {{baseUrl}}/admin/books/1/ingest-json
X-Admin-Api-Key: {{adminKey}}
```

- [ ] **Step 5: Build and verify**

```bash
dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Features/Admin/BooksAdminEndpoints.cs \
        DndMcpAICsharpFun.http
git commit -m "feat: add extract, extracted, ingest-json admin endpoints"
```

---

### Task 9: Service Registration, docker-compose, appsettings

**Files:**
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Modify: `docker-compose.yml`
- Modify: `Config/appsettings.json`

- [ ] **Step 1: Register new services in AddIngestionPipeline**

Add after `services.AddScoped<IIngestionOrchestrator, IngestionOrchestrator>();`:

```csharp
services.AddScoped<ILlmClassifier, OllamaLlmClassifier>();
services.AddScoped<ILlmEntityExtractor, OllamaLlmEntityExtractor>();
services.AddSingleton<IEntityJsonStore, EntityJsonStore>();
services.AddScoped<IJsonIngestionPipeline, JsonIngestionPipeline>();
```

Also add the missing usings at the top of `ServiceCollectionExtensions.cs`:
```csharp
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
```

- [ ] **Step 2: Update docker-compose.yml ollama-pull to pull both models**

Replace the `ollama-pull` service `entrypoint` with a shell script that pulls both models:

```yaml
  ollama-pull:
    image: ollama/ollama:latest
    depends_on:
      ollama:
        condition: service_healthy
    environment:
      - OLLAMA_HOST=http://ollama:11434
    entrypoint: ["/bin/sh", "-c", "ollama pull nomic-embed-text && ollama pull llama3.2"]
    networks:
      - dnd_net
    restart: "no"
```

- [ ] **Step 3: Add ExtractionModel to appsettings.json**

In `Config/appsettings.json`, add `"ExtractionModel": "llama3.2"` inside the `Ollama` section. The section should look like:

```json
"Ollama": {
  "BaseUrl": "http://ollama:11434",
  "EmbeddingModel": "nomic-embed-text",
  "ExtractionModel": "llama3.2"
}
```

- [ ] **Step 4: Build and run all tests**

```bash
dotnet build && dotnet test DndMcpAICsharpFun.Tests -v normal 2>&1 | tail -30
```

Expected: Build succeeded, all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Extensions/ServiceCollectionExtensions.cs \
        docker-compose.yml \
        Config/appsettings.json
git commit -m "feat: register LLM extraction services, pull llama3.2 in docker-compose, add ExtractionModel config"
```

---

## Spec Coverage Checklist

| Spec Requirement | Implemented In |
|---|---|
| ILlmClassifier — Pass 1 classification | Task 2 |
| ILlmEntityExtractor — Pass 2 per-type extraction | Task 3 |
| IEntityJsonStore — page JSON files | Task 4 |
| IJsonIngestionPipeline — Stage 2 orchestration | Task 5 |
| IngestionStatus.Extracted + JsonIngested | Task 1 |
| OllamaOptions.ExtractionModel | Task 1 |
| ExtractedEntity domain model | Task 1 |
| Pass 1 classification prompt | Task 2 |
| Pass 2 extraction prompt with type-specific fields | Task 3 |
| Fallback on invalid Pass 2 JSON → save as Rule partial | Task 3 |
| Merge pass — adjacent page description concatenation | Task 4 |
| POST /admin/books/{id}/extract | Task 8 |
| GET /admin/books/{id}/extracted | Task 8 |
| POST /admin/books/{id}/ingest-json | Task 8 |
| DeleteBook also deletes extracted JSON files | Task 7 |
| llama3.2 pulled by ollama-pull init container | Task 9 |
| Service registration | Task 9 |
| .http file updated | Task 8 |
| MarkExtractedAsync / MarkJsonIngestedAsync | Task 6 |
| Sparse pages skipped in extraction (< 100 chars) | Task 7 |
| Description field embedded; other fields = metadata | Task 5 |
