# TOC-Guided Section Extraction — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace whole-page LLM extraction with TOC-guided, section-grouped extraction — each LLM call targets a heading-bounded block group within a known named section (e.g., "Warlock pages 105–112"), dramatically reducing wrong-shape JSON and enabling Qdrant filtering by section.

**Architecture:** The orchestrator reads the TOC page text, sends it to `OllamaTocMapExtractor` to get a structured map of `{title, category, startPage, endPage}` entries, then for each book page finds its section entry, groups the page's blocks by heading (h1/h2 boundary), and sends each group to `OllamaLlmEntityExtractor` with the entity name and page-range hint. Section metadata flows from `ExtractedEntity` through `EntityJsonStore` JSON files into `ChunkMetadata` and finally into Qdrant payload fields.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, OllamaSharp, Entity Framework Core (SQLite), Qdrant.Client, xUnit, NSubstitute

---

## File Map

**Create:**
- `Domain/TocSectionEntry.cs` — record with Title, Category, StartPage, EndPage
- `Features/Ingestion/Extraction/ITocMapExtractor.cs` — interface
- `Features/Ingestion/Extraction/OllamaTocMapExtractor.cs` — implementation
- `Features/Ingestion/Extraction/PageBlockGrouper.cs` — static grouping helper
- `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocMapExtractorTests.cs`
- `DndMcpAICsharpFun.Tests/Ingestion/Extraction/PageBlockGrouperTests.cs`

**Modify:**
- `Domain/ContentCategory.cs` — add Trait, Lore
- `Domain/ExtractedEntity.cs` — add SectionTitle, SectionStart, SectionEnd
- `Domain/ChunkMetadata.cs` — add SectionTitle, SectionStart, SectionEnd
- `Features/Ingestion/Extraction/TocCategoryMap.cs` — accept TocSectionEntry, add GetEntry
- `Features/Ingestion/Extraction/ILlmEntityExtractor.cs` — add entityName/sectionStartPage/sectionEndPage params
- `Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs` — implement new params, add Trait/Lore TypeFields
- `Features/Ingestion/Extraction/EntityJsonStore.cs` — serialize/deserialize section fields
- `Features/Ingestion/Extraction/JsonIngestionPipeline.cs` — propagate section fields to ChunkMetadata
- `Features/Ingestion/IngestionOrchestrator.cs` — replace ITocCategoryClassifier with ITocMapExtractor, use PageBlockGrouper
- `Features/Admin/BooksAdminEndpoints.cs` — add tocPage, delete RegisterBookByPath
- `Infrastructure/Sqlite/IngestionRecord.cs` — add TocPage int?
- `Infrastructure/Qdrant/QdrantPayloadFields.cs` — add section constants
- `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` — add section indexes
- `Features/VectorStore/QdrantVectorStoreService.cs` — write section payload fields
- `Extensions/ServiceCollectionExtensions.cs` — swap DI registrations
- `DndMcpAICsharpFun.http` — add tocPage, remove register-path
- `DndMcpAICsharpFun.Tests/Ingestion/Extraction/TocCategoryMapTests.cs` — add GetEntry tests
- `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaLlmEntityExtractorTests.cs` — update signature
- `DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs` — update for new flow
- `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs` — add tocPage tests, remove path tests
- `DndMcpAICsharpFun.Tests/Ingestion/Extraction/EntityJsonStoreTests.cs` — add section field tests
- `DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs` — add section propagation test

**Delete:**
- `Features/Ingestion/Extraction/ITocCategoryClassifier.cs`
- `Features/Ingestion/Extraction/OllamaTocCategoryClassifier.cs`
- `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocCategoryClassifierTests.cs`

**Migration:**
- New EF migration `AddTocPageToIngestionRecord`

---

## Task 1: Domain Foundations

**Files:**
- Modify: `Domain/ContentCategory.cs`
- Create: `Domain/TocSectionEntry.cs`
- Modify: `Features/Ingestion/Extraction/TocCategoryMap.cs`
- Modify: `Infrastructure/Sqlite/IngestionRecord.cs`

### Step 1.1: Add Trait and Lore to ContentCategory

- [ ] Edit `Domain/ContentCategory.cs`:

```csharp
namespace DndMcpAICsharpFun.Domain;

public enum ContentCategory
{
    Spell,
    Monster,
    Class,
    Race,
    Background,
    Item,
    Rule,
    Combat,
    Adventuring,
    Condition,
    God,
    Plane,
    Treasure,
    Encounter,
    Trap,
    Trait,
    Lore,
    Unknown
}
```

- [ ] Run `dotnet build` — expect no errors

### Step 1.2: Create TocSectionEntry record

- [ ] Create `Domain/TocSectionEntry.cs`:

```csharp
namespace DndMcpAICsharpFun.Domain;

public sealed record TocSectionEntry(
    string Title,
    ContentCategory? Category,
    int StartPage,
    int? EndPage = null);
```

- [ ] Run `dotnet build` — expect no errors

### Step 1.3: Write failing tests for TocCategoryMap.GetEntry

- [ ] Add tests to `DndMcpAICsharpFun.Tests/Ingestion/Extraction/TocCategoryMapTests.cs` (append after existing tests):

```csharp
// --- GetEntry tests ---

[Fact]
public void GetEntry_ReturnsNull_WhenMapIsEmpty()
{
    var map = new TocCategoryMap([]);
    Assert.Null(map.GetEntry(5));
}

[Fact]
public void GetEntry_ReturnsEntry_WhenPageWithinRange()
{
    var entries = new[]
    {
        new TocSectionEntry("Spells", ContentCategory.Spell, 200, 280),
        new TocSectionEntry("Monsters", ContentCategory.Monster, 300, null)
    };
    var map = new TocCategoryMap(entries);

    var entry = map.GetEntry(250);
    Assert.NotNull(entry);
    Assert.Equal("Spells", entry!.Title);
    Assert.Equal(ContentCategory.Spell, entry.Category);
}

[Fact]
public void GetEntry_ReturnsNull_WhenPageBeforeAllSections()
{
    var map = new TocCategoryMap([new TocSectionEntry("Classes", ContentCategory.Class, 45, 112)]);
    Assert.Null(map.GetEntry(10));
}

[Fact]
public void GetEntry_ReturnsNull_WhenPageAfterSectionEnd()
{
    var map = new TocCategoryMap([new TocSectionEntry("Classes", ContentCategory.Class, 45, 112)]);
    Assert.Null(map.GetEntry(113));
}

[Fact]
public void GetEntry_ComputesMissingEndPage_FromNextEntryStart()
{
    // Spells has no EndPage — should be computed as Monsters.StartPage - 1 = 299
    var entries = new[]
    {
        new TocSectionEntry("Spells", ContentCategory.Spell, 200, null),
        new TocSectionEntry("Monsters", ContentCategory.Monster, 300, null)
    };
    var map = new TocCategoryMap(entries);

    var spellEntry = map.GetEntry(250);
    Assert.NotNull(spellEntry);
    Assert.Equal("Spells", spellEntry!.Title);
    Assert.Equal(299, spellEntry.EndPage);

    Assert.Null(map.GetEntry(299 + 1)); // page 300 belongs to Monsters
    var monsterEntry = map.GetEntry(300);
    Assert.NotNull(monsterEntry);
    Assert.Equal("Monsters", monsterEntry!.Title);
}

[Fact]
public void GetEntry_LastEntryWithNoEndPage_CoversAllHigherPages()
{
    var map = new TocCategoryMap([new TocSectionEntry("Appendix", ContentCategory.Rule, 500, null)]);
    Assert.NotNull(map.GetEntry(99999));
}

[Fact]
public void GetCategory_DelegatesToGetEntry()
{
    var entries = new[] { new TocSectionEntry("Classes", ContentCategory.Class, 45, 112) };
    var map = new TocCategoryMap(entries);
    Assert.Equal(ContentCategory.Class, map.GetCategory(80));
    Assert.Null(map.GetCategory(10));
}
```

- [ ] Run `dotnet test --filter "TocCategoryMapTests"` — expect compile errors (new constructor signature not implemented yet)

### Step 1.4: Implement TocCategoryMap with GetEntry

- [ ] Replace `Features/Ingestion/Extraction/TocCategoryMap.cs` entirely:

```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed class TocCategoryMap
{
    private readonly TocSectionEntry[] _entries;

    public TocCategoryMap(IEnumerable<TocSectionEntry> entries)
    {
        var sorted = entries.OrderBy(static e => e.StartPage).ToArray();
        _entries = new TocSectionEntry[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            var endPage = sorted[i].EndPage
                ?? (i + 1 < sorted.Length ? sorted[i + 1].StartPage - 1 : int.MaxValue);
            _entries[i] = sorted[i] with { EndPage = endPage };
        }
    }

    public bool IsEmpty => _entries.Length == 0;

    public ContentCategory? GetCategory(int pageNumber) => GetEntry(pageNumber)?.Category;

    public TocSectionEntry? GetEntry(int pageNumber)
    {
        foreach (var entry in _entries)
        {
            if (entry.StartPage <= pageNumber && pageNumber <= entry.EndPage!.Value)
                return entry;
        }
        return null;
    }
}
```

- [ ] Run `dotnet test --filter "TocCategoryMapTests"` — expect the OLD tests to fail (constructor signature changed from tuple to TocSectionEntry)

### Step 1.5: Fix old TocCategoryMap tests

The old tests used `(int, ContentCategory?)` tuple constructor. Replace the entire `DndMcpAICsharpFun.Tests/Ingestion/Extraction/TocCategoryMapTests.cs`:

- [ ] Write `DndMcpAICsharpFun.Tests/Ingestion/Extraction/TocCategoryMapTests.cs`:

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public class TocCategoryMapTests
{
    [Fact]
    public void GetCategory_ReturnsNull_WhenNoEntries()
    {
        var map = new TocCategoryMap([]);
        Assert.Null(map.GetCategory(5));
    }

    [Fact]
    public void GetCategory_ReturnsNull_WhenPageBeforeFirstEntry()
    {
        var map = new TocCategoryMap([new TocSectionEntry("Spells", ContentCategory.Spell, 10, 50)]);
        Assert.Null(map.GetCategory(5));
    }

    [Fact]
    public void GetCategory_ReturnsCategory_WhenPageWithinRange()
    {
        var entries = new[]
        {
            new TocSectionEntry("Rules", ContentCategory.Rule, 10, 44),
            new TocSectionEntry("Classes", ContentCategory.Class, 45, 199),
            new TocSectionEntry("Spells", ContentCategory.Spell, 200, 280)
        };
        var map = new TocCategoryMap(entries);
        Assert.Equal(ContentCategory.Class, map.GetCategory(100));
        Assert.Equal(ContentCategory.Spell, map.GetCategory(250));
        Assert.Equal(ContentCategory.Rule, map.GetCategory(10));
    }

    [Fact]
    public void GetCategory_ReturnsNull_WhenCategoryIsNull()
    {
        var entries = new[]
        {
            new TocSectionEntry("Intro", null, 1, 44),
            new TocSectionEntry("Classes", ContentCategory.Class, 45, 199),
        };
        var map = new TocCategoryMap(entries);
        Assert.Null(map.GetCategory(3));
        Assert.Equal(ContentCategory.Class, map.GetCategory(45));
    }

    [Fact]
    public void IsEmpty_True_WhenNoEntries()
    {
        Assert.True(new TocCategoryMap([]).IsEmpty);
    }

    // --- GetEntry tests ---

    [Fact]
    public void GetEntry_ReturnsNull_WhenMapIsEmpty()
    {
        var map = new TocCategoryMap([]);
        Assert.Null(map.GetEntry(5));
    }

    [Fact]
    public void GetEntry_ReturnsEntry_WhenPageWithinRange()
    {
        var entries = new[]
        {
            new TocSectionEntry("Spells", ContentCategory.Spell, 200, 280),
            new TocSectionEntry("Monsters", ContentCategory.Monster, 300, null)
        };
        var map = new TocCategoryMap(entries);

        var entry = map.GetEntry(250);
        Assert.NotNull(entry);
        Assert.Equal("Spells", entry!.Title);
        Assert.Equal(ContentCategory.Spell, entry.Category);
    }

    [Fact]
    public void GetEntry_ReturnsNull_WhenPageBeforeAllSections()
    {
        var map = new TocCategoryMap([new TocSectionEntry("Classes", ContentCategory.Class, 45, 112)]);
        Assert.Null(map.GetEntry(10));
    }

    [Fact]
    public void GetEntry_ReturnsNull_WhenPageAfterSectionEnd()
    {
        var map = new TocCategoryMap([new TocSectionEntry("Classes", ContentCategory.Class, 45, 112)]);
        Assert.Null(map.GetEntry(113));
    }

    [Fact]
    public void GetEntry_ComputesMissingEndPage_FromNextEntryStart()
    {
        var entries = new[]
        {
            new TocSectionEntry("Spells", ContentCategory.Spell, 200, null),
            new TocSectionEntry("Monsters", ContentCategory.Monster, 300, null)
        };
        var map = new TocCategoryMap(entries);

        var spellEntry = map.GetEntry(250);
        Assert.NotNull(spellEntry);
        Assert.Equal(299, spellEntry!.EndPage);

        Assert.Null(map.GetEntry(299 + 1));
        Assert.Equal("Monsters", map.GetEntry(300)!.Title);
    }

    [Fact]
    public void GetEntry_LastEntryWithNoEndPage_CoversAllHigherPages()
    {
        var map = new TocCategoryMap([new TocSectionEntry("Appendix", ContentCategory.Rule, 500, null)]);
        Assert.NotNull(map.GetEntry(99999));
    }
}
```

- [ ] Run `dotnet test --filter "TocCategoryMapTests"` — all tests pass

### Step 1.6: Add TocPage to IngestionRecord

- [ ] Edit `Infrastructure/Sqlite/IngestionRecord.cs` — add property after `CreatedAt`:

```csharp
public int? TocPage { get; set; }
```

Full file after edit:

```csharp
using System.ComponentModel.DataAnnotations;

namespace DndMcpAICsharpFun.Infrastructure.Sqlite;

public sealed class IngestionRecord
{
    public int Id { get; set; }

    [Required, MaxLength(1024)]
    public string FilePath { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string FileHash { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string SourceName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Version { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    public IngestionStatus Status { get; set; } = IngestionStatus.Pending;

    public string? Error { get; set; }

    public int? ChunkCount { get; set; }

    public DateTime? IngestedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? TocPage { get; set; }
}
```

### Step 1.7: Add EF Core migration

- [ ] Run:

```bash
dotnet ef migrations add AddTocPageToIngestionRecord
```

Expected: new file `Migrations/YYYYMMDDHHMMSS_AddTocPageToIngestionRecord.cs` created.

- [ ] Verify the generated migration adds the column:

```csharp
migrationBuilder.AddColumn<int>(
    name: "TocPage",
    table: "IngestionRecords",
    type: "INTEGER",
    nullable: true);
```

- [ ] Run `dotnet build` — expect no errors

### Step 1.8: Commit

- [ ] Commit:

```bash
git add Domain/ContentCategory.cs Domain/TocSectionEntry.cs \
    Features/Ingestion/Extraction/TocCategoryMap.cs \
    Infrastructure/Sqlite/IngestionRecord.cs \
    Migrations/ \
    DndMcpAICsharpFun.Tests/Ingestion/Extraction/TocCategoryMapTests.cs
git commit -m "feat: add Trait/Lore categories, TocSectionEntry, TocCategoryMap.GetEntry, TocPage migration"
```

---

## Task 2: TOC Map Extractor

**Files:**
- Create: `Features/Ingestion/Extraction/ITocMapExtractor.cs`
- Create: `Features/Ingestion/Extraction/OllamaTocMapExtractor.cs`
- Create: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocMapExtractorTests.cs`
- Delete: `Features/Ingestion/Extraction/ITocCategoryClassifier.cs`
- Delete: `Features/Ingestion/Extraction/OllamaTocCategoryClassifier.cs`
- Delete: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocCategoryClassifierTests.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs`

### Step 2.1: Write failing tests for OllamaTocMapExtractor

- [ ] Create `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocMapExtractorTests.cs`:

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public class OllamaTocMapExtractorTests
{
    private static OllamaOptions DefaultOptions() => new() { ExtractionModel = "llama3.2" };

    private static OllamaTocMapExtractor BuildSut(IOllamaApiClient ollama) =>
        new(ollama, Options.Create(DefaultOptions()), NullLogger<OllamaTocMapExtractor>.Instance);

    [Fact]
    public async Task ExtractMapAsync_ReturnsEmptyList_WhenTocTextIsEmpty()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        var sut = BuildSut(ollama);

        var result = await sut.ExtractMapAsync(string.Empty);

        Assert.Empty(result);
        ollama.DidNotReceive().ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractMapAsync_ReturnsEmptyList_WhenLlmReturnsInvalidJson()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsyncChunks("not json"));

        var sut = BuildSut(ollama);

        var result = await sut.ExtractMapAsync("Table of Contents...");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractMapAsync_ParsesValidLlmResponse_ReturnsEntries()
    {
        var json = """
            [
              {"title":"Classes","category":"Class","startPage":45,"endPage":112},
              {"title":"Spells","category":"Spell","startPage":200,"endPage":null}
            ]
            """;
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsyncChunks(json));

        var sut = BuildSut(ollama);

        var result = await sut.ExtractMapAsync("Table of Contents\nClasses ... 45\nSpells ... 200");

        Assert.Equal(2, result.Count);
        Assert.Equal("Classes", result[0].Title);
        Assert.Equal(ContentCategory.Class, result[0].Category);
        Assert.Equal(45, result[0].StartPage);
        Assert.Equal(112, result[0].EndPage);

        Assert.Equal("Spells", result[1].Title);
        Assert.Equal(ContentCategory.Spell, result[1].Category);
        Assert.Equal(200, result[1].StartPage);
        Assert.Null(result[1].EndPage); // null passed through; TocCategoryMap fills it
    }

    [Fact]
    public async Task ExtractMapAsync_ParsesNullCategory_AsNullCategory()
    {
        var json = """[{"title":"Introduction","category":null,"startPage":1,"endPage":10}]""";
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsyncChunks(json));

        var sut = BuildSut(ollama);

        var result = await sut.ExtractMapAsync("intro text");

        Assert.Single(result);
        Assert.Null(result[0].Category);
    }

    [Fact]
    public async Task ExtractMapAsync_SkipsEntriesWithZeroStartPage()
    {
        var json = """
            [
              {"title":"Bad","category":"Rule","startPage":0,"endPage":5},
              {"title":"Good","category":"Rule","startPage":10,"endPage":20}
            ]
            """;
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsyncChunks(json));

        var sut = BuildSut(ollama);

        var result = await sut.ExtractMapAsync("toc text");

        Assert.Single(result);
        Assert.Equal(10, result[0].StartPage);
    }

    private static async IAsyncEnumerable<ChatResponseStream?> AsyncChunks(string text)
    {
        yield return new ChatResponseStream { Message = new Message { Content = text } };
        await Task.CompletedTask;
    }
}
```

- [ ] Run `dotnet test --filter "OllamaTocMapExtractorTests"` — expect compile errors (class doesn't exist yet)

### Step 2.2: Create ITocMapExtractor interface

- [ ] Create `Features/Ingestion/Extraction/ITocMapExtractor.cs`:

```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface ITocMapExtractor
{
    Task<IReadOnlyList<TocSectionEntry>> ExtractMapAsync(
        string tocPageText,
        CancellationToken ct = default);
}
```

### Step 2.3: Implement OllamaTocMapExtractor

- [ ] Create `Features/Ingestion/Extraction/OllamaTocMapExtractor.cs`:

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed partial class OllamaTocMapExtractor(
    IOllamaApiClient ollama,
    IOptions<OllamaOptions> options,
    ILogger<OllamaTocMapExtractor> logger) : ITocMapExtractor
{
    private const string SystemPrompt =
        """
        You are a D&D rulebook table-of-contents parser.
        Given raw TOC page text, extract all chapter or section entries.

        Return a JSON array. Each element must have exactly these keys:
        - "title": string (chapter or section title)
        - "category": one of Spell, Monster, Class, Race, Background, Item, Rule, Combat,
          Adventuring, Condition, God, Plane, Treasure, Encounter, Trap, Trait, Lore, or null
        - "startPage": integer (page number where this section begins)
        - "endPage": integer or null (page where this section ends, null if not stated)

        Category guidance:
        "Spells"/"Spell Descriptions" → Spell
        "Monsters"/"Bestiary" → Monster
        "Classes"/"Class Features" → Class
        "Races"/"Species" → Race
        "Backgrounds" → Background
        "Equipment"/"Items"/"Magic Items" → Item
        "Combat"/"Actions in Combat" → Combat
        "Adventuring"/"Resting" → Adventuring
        "Conditions" → Condition
        "Gods"/"Deities"/"Pantheon" → God
        "Planes"/"Cosmology" → Plane
        "Treasure" → Treasure
        "Encounters" → Encounter
        "Traps" → Trap
        "Traits"/"Character Traits" → Trait
        "Lore"/"History"/"World Background" → Lore
        Introduction, Preface, Index, Appendix, Contents → null
        Ability scores, skills, proficiency, rules → Rule

        Reply with JSON only, no explanation.
        """;

    private readonly string _model = options.Value.ExtractionModel;
    private readonly int _numCtx = options.Value.ExtractionNumCtx;

    public async Task<IReadOnlyList<TocSectionEntry>> ExtractMapAsync(
        string tocPageText,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tocPageText))
            return [];

        var request = new ChatRequest
        {
            Model = _model,
            Stream = true,
            Format = "json",
            Options = new OllamaSharp.Models.RequestOptions { NumCtx = _numCtx },
            Messages =
            [
                new Message { Role = ChatRole.System, Content = SystemPrompt },
                new Message { Role = ChatRole.User, Content = tocPageText }
            ]
        };

        var sb = new StringBuilder();
        await foreach (var chunk in ollama.ChatAsync(request, ct))
            sb.Append(chunk?.Message?.Content ?? string.Empty);

        var json = sb.ToString().Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var start = json.IndexOf('\n') + 1;
            var end = json.LastIndexOf("```", StringComparison.Ordinal);
            if (end > start) json = json[start..end].Trim();
        }

        try
        {
            var array = JsonNode.Parse(json)?.AsArray();
            if (array is null)
            {
                LogInvalidJson(logger, json[..Math.Min(200, json.Length)]);
                return [];
            }

            var entries = new List<TocSectionEntry>(array.Count);
            foreach (var node in array)
            {
                if (node is not JsonObject obj) continue;

                var title = obj["title"]?.GetValue<string>() ?? string.Empty;
                var categoryStr = obj["category"]?.GetValue<string>();
                ContentCategory? category = Enum.TryParse<ContentCategory>(categoryStr, out var c) ? c : null;
                var startPage = obj["startPage"]?.GetValue<int>() ?? 0;

                int? endPage = null;
                if (obj["endPage"] is JsonValue endVal && endVal.TryGetValue<int>(out var ep))
                    endPage = ep;

                if (startPage > 0)
                    entries.Add(new TocSectionEntry(title, category, startPage, endPage));
            }

            LogExtracted(logger, entries.Count, _model);
            return entries;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            LogInvalidJson(logger, json[..Math.Min(200, json.Length)]);
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "TOC map extracted {Count} entries with {Model}")]
    private static partial void LogExtracted(ILogger logger, int count, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TOC map extractor returned invalid JSON: {Json}")]
    private static partial void LogInvalidJson(ILogger logger, string json);
}
```

- [ ] Run `dotnet test --filter "OllamaTocMapExtractorTests"` — all 5 tests pass

### Step 2.4: Delete old classifier files

- [ ] Delete `Features/Ingestion/Extraction/ITocCategoryClassifier.cs`
- [ ] Delete `Features/Ingestion/Extraction/OllamaTocCategoryClassifier.cs`
- [ ] Delete `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocCategoryClassifierTests.cs`

### Step 2.5: Update DI registration

- [ ] Edit `Extensions/ServiceCollectionExtensions.cs` — in `AddIngestionPipeline`, replace:

```csharp
        services.AddSingleton<IPdfBookmarkReader, PdfPigBookmarkReader>();
        services.AddSingleton<ITocCategoryClassifier, OllamaTocCategoryClassifier>();
```

with:

```csharp
        services.AddSingleton<ITocMapExtractor, OllamaTocMapExtractor>();
```

- [ ] Run `dotnet build` — expect errors in `IngestionOrchestrator.cs` (still references old types — fixed in Task 4)

### Step 2.6: Commit

- [ ] Commit:

```bash
git add Features/Ingestion/Extraction/ITocMapExtractor.cs \
    Features/Ingestion/Extraction/OllamaTocMapExtractor.cs \
    Extensions/ServiceCollectionExtensions.cs \
    DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocMapExtractorTests.cs
git rm Features/Ingestion/Extraction/ITocCategoryClassifier.cs \
    Features/Ingestion/Extraction/OllamaTocCategoryClassifier.cs \
    DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocCategoryClassifierTests.cs
git commit -m "feat: replace OllamaTocCategoryClassifier with OllamaTocMapExtractor"
```

---

## Task 3: Section Grouper

**Files:**
- Create: `Features/Ingestion/Extraction/PageBlockGrouper.cs`
- Create: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/PageBlockGrouperTests.cs`

### Step 3.1: Write failing tests for PageBlockGrouper

- [ ] Create `DndMcpAICsharpFun.Tests/Ingestion/Extraction/PageBlockGrouperTests.cs`:

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public class PageBlockGrouperTests
{
    private static PageBlock H1(string text) => new(1, "h1", text);
    private static PageBlock H2(string text) => new(1, "h2", text);
    private static PageBlock H3(string text) => new(1, "h3", text);
    private static PageBlock Body(string text) => new(1, "body", text);

    [Fact]
    public void Group_EmptyBlocks_ReturnsEmptyList()
    {
        var result = PageBlockGrouper.Group([]);
        Assert.Empty(result);
    }

    [Fact]
    public void Group_OnlyBodyBlocks_ReturnsSingleGroup()
    {
        var blocks = new[] { Body("para 1"), Body("para 2") };
        var result = PageBlockGrouper.Group(blocks);
        Assert.Single(result);
        Assert.Equal(2, result[0].Count);
    }

    [Fact]
    public void Group_H2StartsNewGroup()
    {
        var blocks = new[] { H2("Spells"), Body("spell info"), H2("Monsters"), Body("monster info") };
        var result = PageBlockGrouper.Group(blocks);
        Assert.Equal(2, result.Count);
        Assert.Equal("Spells", result[0][0].Text);
        Assert.Equal("Monsters", result[1][0].Text);
    }

    [Fact]
    public void Group_H1StartsNewGroup()
    {
        var blocks = new[] { H1("Chapter 1"), Body("intro"), H1("Chapter 2"), Body("content") };
        var result = PageBlockGrouper.Group(blocks);
        Assert.Equal(2, result.Count);
        Assert.Equal("Chapter 1", result[0][0].Text);
    }

    [Fact]
    public void Group_H3AppendedToCurrentGroup()
    {
        var blocks = new[] { H2("Warlock"), H3("Eldritch Invocations"), Body("invoc text") };
        var result = PageBlockGrouper.Group(blocks);
        Assert.Single(result);
        Assert.Equal(3, result[0].Count);
    }

    [Fact]
    public void Group_BodyBeforeAnyHeading_GoesIntoLeadingGroup()
    {
        var blocks = new[] { Body("preamble"), H2("Section A"), Body("section text") };
        var result = PageBlockGrouper.Group(blocks);
        Assert.Equal(2, result.Count);
        Assert.Single(result[0]); // preamble
        Assert.Equal(2, result[1].Count); // H2 + body
    }

    [Fact]
    public void Group_MultipleH2s_EachStartsNewGroup()
    {
        var blocks = new[]
        {
            H2("Wizard"), Body("wizard text"),
            H2("Warlock"), Body("warlock text"),
            H2("Sorcerer"), Body("sorcerer text")
        };
        var result = PageBlockGrouper.Group(blocks);
        Assert.Equal(3, result.Count);
        Assert.All(result, g => Assert.Equal(2, g.Count));
    }
}
```

- [ ] Run `dotnet test --filter "PageBlockGrouperTests"` — expect compile errors

### Step 3.2: Implement PageBlockGrouper

- [ ] Create `Features/Ingestion/Extraction/PageBlockGrouper.cs`:

```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public static class PageBlockGrouper
{
    public static IReadOnlyList<IReadOnlyList<PageBlock>> Group(IReadOnlyList<PageBlock> blocks)
    {
        var groups = new List<List<PageBlock>>();
        List<PageBlock>? current = null;

        foreach (var block in blocks)
        {
            if (block.Level is "h1" or "h2")
            {
                current = [block];
                groups.Add(current);
            }
            else
            {
                if (current is null)
                {
                    current = [];
                    groups.Add(current);
                }
                current.Add(block);
            }
        }

        return groups;
    }
}
```

- [ ] Run `dotnet test --filter "PageBlockGrouperTests"` — all 7 tests pass

### Step 3.3: Commit

```bash
git add Features/Ingestion/Extraction/PageBlockGrouper.cs \
    DndMcpAICsharpFun.Tests/Ingestion/Extraction/PageBlockGrouperTests.cs
git commit -m "feat: add PageBlockGrouper for h1/h2-bounded section grouping"
```

---

## Task 4: Extraction Pipeline Changes

**Files:**
- Modify: `Features/Ingestion/Extraction/ILlmEntityExtractor.cs`
- Modify: `Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs`
- Modify: `Features/Ingestion/IngestionOrchestrator.cs`
- Modify: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaLlmEntityExtractorTests.cs`
- Modify: `DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs`

### Step 4.1: Update ILlmEntityExtractor interface

- [ ] Replace `Features/Ingestion/Extraction/ILlmEntityExtractor.cs`:

```csharp
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
        string entityName,
        int sectionStartPage,
        int sectionEndPage,
        CancellationToken ct = default);
}
```

- [ ] Run `dotnet build` — expect errors in `OllamaLlmEntityExtractor`, `IngestionOrchestrator`, and test files

### Step 4.2: Add Trait/Lore TypeFields and update OllamaLlmEntityExtractor

- [ ] Edit `Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs`:

In `TypeFields`, add after `["Trap"]` line:

```csharp
        ["Trait"]       = "description (string), source_category (string)",
        ["Lore"]        = "description (string)",
```

Change the `ExtractAsync` signature to:

```csharp
    public async Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(
        string pageText,
        string entityType,
        int pageNumber,
        string sourceBook,
        string version,
        string entityName,
        int sectionStartPage,
        int sectionEndPage,
        CancellationToken ct = default)
```

Change the system prompt to include the context hint:

```csharp
        var systemPrompt =
            $"""
            You are a D&D 5e content extractor. Extract all {entityType} entities from the section text below.
            This is a section from the {entityName} {entityType} (pages {sectionStartPage}–{sectionEndPage}).

            OUTPUT RULES — follow exactly:
            1. Reply with a JSON ARRAY and nothing else. No object wrapper, no prose, no section headings as keys.
            2. Each array element must have exactly these keys: "name" (string), "partial" (bool), "data" (object).
            3. "partial" is true only if the entity is cut off at the page boundary.
            4. Use null for any missing fields inside "data".
            5. If there are no {entityType} entities on this page, return exactly: []

            Fields for {entityType}: {fields}
            """;
```

- [ ] Run `dotnet build` — expect errors only in test files and IngestionOrchestrator

### Step 4.3: Update OllamaLlmEntityExtractorTests

- [ ] Replace `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaLlmEntityExtractorTests.cs`:

```csharp
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public class OllamaLlmEntityExtractorTests
{
    private static OllamaOptions DefaultOllamaOptions() => new() { ExtractionModel = "llama3.2" };

    private static OllamaLlmEntityExtractor BuildSut(
        IOllamaApiClient ollama,
        int llmExtractionRetries = 1)
    {
        var ingestionOptions = new IngestionOptions { LlmExtractionRetries = llmExtractionRetries };
        return new OllamaLlmEntityExtractor(
            ollama,
            Options.Create(DefaultOllamaOptions()),
            Options.Create(ingestionOptions),
            NullLogger<OllamaLlmEntityExtractor>.Instance);
    }

    private static Task<IReadOnlyList<ExtractedEntity>> Extract(
        OllamaLlmEntityExtractor sut,
        string pageText = "page text",
        string entityType = "Spell",
        int pageNumber = 1,
        string entityName = "Spells",
        int startPage = 200,
        int endPage = 250) =>
        sut.ExtractAsync(pageText, entityType, pageNumber, "PHB", "5e", entityName, startPage, endPage);

    [Fact]
    public async Task ExtractAsync_ValidJsonFirstAttempt_ReturnsEntityWithoutRetry()
    {
        var json = """[{"name":"Fireball","partial":false,"data":{"description":"test"}}]""";
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse(json));

        var sut = BuildSut(ollama, llmExtractionRetries: 1);

        var results = await Extract(sut, entityName: "Spells", startPage: 200, endPage: 250);

        Assert.Single(results);
        Assert.Equal("Fireball", results[0].Name);
        ollama.Received(1).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_InvalidThenValid_RetrySucceeds()
    {
        var validJson = """[{"name":"Fireball","partial":false,"data":{"description":"test"}}]""";
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse("not valid json"), StreamResponse(validJson));

        var sut = BuildSut(ollama, llmExtractionRetries: 1);

        var results = await Extract(sut, pageNumber: 2);

        Assert.Single(results);
        Assert.Equal("Fireball", results[0].Name);
        ollama.Received(2).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_AllAttemptsInvalid_ReturnsEmpty()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse("not valid json"));

        var sut = BuildSut(ollama, llmExtractionRetries: 1);

        var results = await Extract(sut, pageNumber: 3);

        Assert.Empty(results);
        ollama.Received(2).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_ZeroRetries_SingleAttemptOnly()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse("not valid json"));

        var sut = BuildSut(ollama, llmExtractionRetries: 0);

        var results = await Extract(sut, pageNumber: 4);

        Assert.Empty(results);
        ollama.Received(1).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_WrappedJsonObject_UnwrapsAndParsesSuccessfully()
    {
        var json = """{"entities":[{"name":"Fireball","partial":false,"data":{"description":"test"}}]}""";
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse(json));

        var sut = BuildSut(ollama, llmExtractionRetries: 1);

        var results = await Extract(sut);

        Assert.Single(results);
        Assert.Equal("Fireball", results[0].Name);
        ollama.Received(1).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_ContextHint_AppearsInSystemPrompt()
    {
        var capturedRequest = (ChatRequest?)null;
        var json = """[{"name":"Warlock","partial":false,"data":{"description":"test"}}]""";
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Do<ChatRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
            .Returns(StreamResponse(json));

        var sut = BuildSut(ollama, llmExtractionRetries: 0);

        await sut.ExtractAsync("page text", "Class", 106, "PHB", "5e", "Warlock", 105, 112);

        Assert.NotNull(capturedRequest);
        var systemMsg = capturedRequest!.Messages!.First(m => m.Role == ChatRole.System).Content;
        Assert.Contains("Warlock", systemMsg);
        Assert.Contains("105", systemMsg);
        Assert.Contains("112", systemMsg);
    }

    private static IAsyncEnumerable<ChatResponseStream?> StreamResponse(string content)
        => AsyncEnumerable(new ChatResponseStream { Message = new Message { Content = content } });

    private static async IAsyncEnumerable<T> AsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }
}
```

- [ ] Run `dotnet test --filter "OllamaLlmEntityExtractorTests"` — all 6 tests pass

### Step 4.4: Update IngestionOrchestrator

- [ ] Replace `Features/Ingestion/IngestionOrchestrator.cs` entirely:

```csharp
using System.Security.Cryptography;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed partial class IngestionOrchestrator(
    IIngestionTracker tracker,
    IPdfStructuredExtractor extractor,
    IVectorStoreService vectorStore,
    ILlmEntityExtractor entityExtractor,
    IEntityJsonStore jsonStore,
    IJsonIngestionPipeline jsonPipeline,
    IOptions<IngestionOptions> ingestionOptions,
    ITocMapExtractor tocMapExtractor,
    ILogger<IngestionOrchestrator> logger) : IIngestionOrchestrator
{
    public async Task ExtractBookAsync(int recordId, CancellationToken cancellationToken = default)
    {
        var record = await tracker.GetByIdAsync(recordId, cancellationToken);
        if (record is null)
        {
            LogRecordNotFound(logger, recordId);
            return;
        }

        if (record.TocPage is null)
        {
            LogTocPageMissing(logger, record.DisplayName, recordId);
            await tracker.MarkFailedAsync(recordId, "TocPage is required for extraction.", CancellationToken.None);
            return;
        }

        LogStartingExtraction(logger, record.DisplayName, recordId);

        try
        {
            var currentHash = string.IsNullOrEmpty(record.FileHash)
                ? await ComputeHashAsync(record.FilePath, cancellationToken)
                : record.FileHash;
            await tracker.MarkHashAsync(recordId, currentHash, cancellationToken);

            if (record.Status == IngestionStatus.JsonIngested)
            {
                await vectorStore.DeleteByHashAsync(record.FileHash, record.ChunkCount!.Value, CancellationToken.None);
                jsonStore.DeleteAllPages(recordId);
                await tracker.ResetForReingestionAsync(recordId, CancellationToken.None);
            }
            else if (record.Status == IngestionStatus.Extracted)
            {
                jsonStore.DeleteAllPages(recordId);
                await tracker.ResetForReingestionAsync(recordId, CancellationToken.None);
            }

            var tocPage = extractor.ExtractSinglePage(record.FilePath, record.TocPage.Value);
            if (tocPage is null)
            {
                LogTocPageNotFound(logger, record.TocPage.Value, record.DisplayName, recordId);
                await tracker.MarkFailedAsync(recordId, $"TOC page {record.TocPage.Value} could not be extracted.", CancellationToken.None);
                return;
            }

            var tocEntries = await tocMapExtractor.ExtractMapAsync(tocPage.RawText, cancellationToken);
            var tocMap = new TocCategoryMap(tocEntries);

            if (tocMap.IsEmpty)
                LogTocFallback(logger, record.DisplayName, recordId);
            else
                LogTocGuided(logger, record.DisplayName, recordId);

            var pages = extractor.ExtractPages(record.FilePath).ToList();

            foreach (var structuredPage in pages)
            {
                if (structuredPage.RawText.Length < ingestionOptions.Value.MinPageCharacters)
                    continue;

                var entry = tocMap.GetEntry(structuredPage.PageNumber);
                if (entry is null)
                    continue;

                var groups = PageBlockGrouper.Group(structuredPage.Blocks);
                var pageEntities = new List<ExtractedEntity>();

                foreach (var group in groups)
                {
                    var promptText = BuildPromptText(group);
                    var entityType = entry.Category?.ToString() ?? "Rule";
                    var extracted = await entityExtractor.ExtractAsync(
                        promptText, entityType, structuredPage.PageNumber,
                        record.DisplayName, record.Version,
                        entry.Title, entry.StartPage, entry.EndPage!.Value,
                        cancellationToken);
                    pageEntities.AddRange(extracted);
                }

                await jsonStore.SavePageAsync(recordId, structuredPage, pageEntities, cancellationToken);
                LogExtractedPage(logger, structuredPage.PageNumber, pages.Count, recordId);
            }

            await tracker.MarkExtractedAsync(recordId, cancellationToken);
            LogExtractedBook(logger, record.DisplayName, recordId);
        }
        catch (OperationCanceledException)
        {
            jsonStore.DeleteAllPages(recordId);
            await tracker.ResetForReingestionAsync(recordId, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            LogExtractionFailed(logger, ex, record.DisplayName, recordId);
            await tracker.MarkFailedAsync(recordId, ex.Message, CancellationToken.None);
        }
    }

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

            var pages = await jsonStore.LoadAllPagesAsync(recordId, cancellationToken);
            var chunkCount = pages.Sum(p => p.Entities.Count(e =>
                !string.IsNullOrWhiteSpace(e.Data["description"]?.GetValue<string>())));

            await tracker.MarkJsonIngestedAsync(recordId, chunkCount, cancellationToken);
            LogJsonIngested(logger, record.DisplayName, recordId, chunkCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogJsonIngestionFailed(logger, ex, record.DisplayName, recordId);
            await tracker.MarkFailedAsync(recordId, ex.Message, CancellationToken.None);
        }
    }

    public async Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken cancellationToken = default)
    {
        var record = await tracker.GetByIdAsync(id, cancellationToken);
        if (record is null)
            return DeleteBookResult.NotFound;

        if (record.Status == IngestionStatus.Processing)
            return DeleteBookResult.Conflict;

        if (record.Status == IngestionStatus.JsonIngested && record.ChunkCount.HasValue)
            await vectorStore.DeleteByHashAsync(record.FileHash, record.ChunkCount.Value, cancellationToken);

        jsonStore.DeleteAllPages(id);

        if (File.Exists(record.FilePath))
            File.Delete(record.FilePath);

        await tracker.DeleteAsync(id, cancellationToken);
        LogBookDeleted(logger, record.DisplayName, id);
        return DeleteBookResult.Deleted;
    }

    public async Task<PageData?> ExtractSinglePageAsync(
        int bookId, int pageNumber, bool save, CancellationToken ct = default)
    {
        var record = await tracker.GetByIdAsync(bookId, ct);
        if (record is null) return null;

        var structuredPage = extractor.ExtractSinglePage(record.FilePath, pageNumber);
        if (structuredPage is null) return null;

        List<ExtractedEntity> entities = [];

        if (record.TocPage.HasValue)
        {
            var tocPage = extractor.ExtractSinglePage(record.FilePath, record.TocPage.Value);
            if (tocPage is not null)
            {
                var tocEntries = await tocMapExtractor.ExtractMapAsync(tocPage.RawText, ct);
                var tocMap = new TocCategoryMap(tocEntries);
                var entry = tocMap.GetEntry(pageNumber);

                if (entry is not null)
                {
                    var groups = PageBlockGrouper.Group(structuredPage.Blocks);
                    foreach (var group in groups)
                    {
                        var promptText = BuildPromptText(group);
                        var entityType = entry.Category?.ToString() ?? "Rule";
                        var extracted = await entityExtractor.ExtractAsync(
                            promptText, entityType, pageNumber,
                            record.DisplayName, record.Version,
                            entry.Title, entry.StartPage, entry.EndPage!.Value, ct);
                        entities.AddRange(extracted);
                    }
                }
            }
        }

        var pageData = new PageData(pageNumber, structuredPage.RawText, structuredPage.Blocks, entities);

        if (save)
            await jsonStore.SavePageAsync(bookId, structuredPage, entities, ct);

        return pageData;
    }

    internal static string BuildPromptText(IReadOnlyList<PageBlock> blocks)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in blocks)
        {
            var prefix = block.Level switch
            {
                "h1" => "[H1] ",
                "h2" => "[H2] ",
                "h3" => "[H3] ",
                _    => string.Empty
            };
            sb.AppendLine(prefix + block.Text);
        }
        return sb.ToString().TrimEnd();
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ingestion record {Id} not found")]
    private static partial void LogRecordNotFound(ILogger logger, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TocPage is not set for {DisplayName} (id={Id}) — extraction requires tocPage")]
    private static partial void LogTocPageMissing(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TOC page {TocPage} not found in PDF for {DisplayName} (id={Id})")]
    private static partial void LogTocPageNotFound(ILogger logger, int tocPage, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted book {DisplayName} (id={Id})")]
    private static partial void LogBookDeleted(ILogger logger, string displayName, int id);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted page {Page}/{Total} for book {BookId}")]
    private static partial void LogExtractedPage(ILogger logger, int page, int total, int bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "TOC-guided extraction active for {DisplayName} (id={Id})")]
    private static partial void LogTocGuided(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TOC map empty for {DisplayName} (id={Id}) — all pages will be skipped")]
    private static partial void LogTocFallback(ILogger logger, string displayName, int id);
}
```

- [ ] Run `dotnet build` — expect errors only in `IngestionOrchestratorTests` (old mock types)

### Step 4.5: Update IngestionOrchestratorTests

The old tests reference `ITocCategoryClassifier`, `IPdfBookmarkReader`, and `ILlmClassifier` which are no longer in the orchestrator. Replace the test file:

- [ ] Replace `DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class IngestionOrchestratorTests : IDisposable
{
    private readonly IIngestionTracker _tracker = Substitute.For<IIngestionTracker>();
    private readonly IPdfStructuredExtractor _extractor = Substitute.For<IPdfStructuredExtractor>();
    private readonly IVectorStoreService _vectorStore = Substitute.For<IVectorStoreService>();
    private readonly ILlmEntityExtractor _entityExtractor = Substitute.For<ILlmEntityExtractor>();
    private readonly IEntityJsonStore _jsonStore = Substitute.For<IEntityJsonStore>();
    private readonly IJsonIngestionPipeline _jsonPipeline = Substitute.For<IJsonIngestionPipeline>();
    private readonly ITocMapExtractor _tocMapExtractor = Substitute.For<ITocMapExtractor>();
    private readonly string _tempFile;

    public IngestionOrchestratorTests()
    {
        _tempFile = Path.GetTempFileName();
        File.WriteAllText(_tempFile, "dummy pdf content");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    private IngestionOrchestrator BuildSut()
    {
        var opts = Options.Create(new IngestionOptions { MaxChunkTokens = 512, OverlapTokens = 64 });
        return new IngestionOrchestrator(
            _tracker, _extractor, _vectorStore,
            _entityExtractor, _jsonStore, _jsonPipeline, opts,
            _tocMapExtractor,
            NullLogger<IngestionOrchestrator>.Instance);
    }

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, CancellationToken.None);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private IngestionRecord MakeRecord(
        int id = 1,
        IngestionStatus status = IngestionStatus.Pending,
        int? tocPage = 3) => new()
    {
        Id = id,
        FilePath = _tempFile,
        FileName = "test.pdf",
        FileHash = string.Empty,
        SourceName = "PHB",
        Version = "Edition2014",
        DisplayName = "PHB",
        Status = status,
        TocPage = tocPage,
    };

    // ── Delete ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteBookAsync_RecordNotFound_ReturnsNotFound()
    {
        _tracker.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);
        var sut = BuildSut();

        var result = await sut.DeleteBookAsync(99);

        Assert.Equal(DeleteBookResult.NotFound, result);
        await _vectorStore.DidNotReceive().DeleteByHashAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteBookAsync_RecordProcessing_ReturnsConflict()
    {
        _tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(1, IngestionStatus.Processing));
        var sut = BuildSut();

        var result = await sut.DeleteBookAsync(1);

        Assert.Equal(DeleteBookResult.Conflict, result);
        await _vectorStore.DidNotReceive().DeleteByHashAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteBookAsync_JsonIngestedRecord_DeletesVectorsFileSqlite()
    {
        var record = MakeRecord(2, IngestionStatus.JsonIngested);
        record.FileHash = "abc123";
        record.ChunkCount = 10;
        _tracker.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(record);
        _tracker.DeleteAsync(2, Arg.Any<CancellationToken>()).Returns(true);
        var sut = BuildSut();

        var result = await sut.DeleteBookAsync(2);

        Assert.Equal(DeleteBookResult.Deleted, result);
        await _vectorStore.Received(1).DeleteByHashAsync("abc123", 10, Arg.Any<CancellationToken>());
        _jsonStore.Received(1).DeleteAllPages(2);
        await _tracker.Received(1).DeleteAsync(2, Arg.Any<CancellationToken>());
        Assert.False(File.Exists(_tempFile));
    }

    [Fact]
    public async Task DeleteBookAsync_PendingRecord_SkipsVectorsDeletesSqlite()
    {
        _tracker.GetByIdAsync(5, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(5, IngestionStatus.Pending));
        _tracker.DeleteAsync(5, Arg.Any<CancellationToken>()).Returns(true);
        var sut = BuildSut();

        var result = await sut.DeleteBookAsync(5);

        Assert.Equal(DeleteBookResult.Deleted, result);
        await _vectorStore.DidNotReceive().DeleteByHashAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _tracker.Received(1).DeleteAsync(5, Arg.Any<CancellationToken>());
    }

    // ── Extract ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractBookAsync_RecordNotFound_Returns()
    {
        _tracker.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);
        var sut = BuildSut();

        await sut.ExtractBookAsync(999);

        await _tracker.Received(1).GetByIdAsync(999, Arg.Any<CancellationToken>());
        _extractor.DidNotReceive().ExtractPages(Arg.Any<string>());
    }

    [Fact]
    public async Task ExtractBookAsync_TocPageNull_MarksFailedWithoutExtraction()
    {
        _tracker.GetByIdAsync(10, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(10, tocPage: null));
        var sut = BuildSut();

        await sut.ExtractBookAsync(10);

        _extractor.DidNotReceive().ExtractPages(Arg.Any<string>());
        await _tracker.Received(1).MarkFailedAsync(10, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_NoHash_ComputesAndSetsHash()
    {
        var record = MakeRecord(22, tocPage: 1);
        _tracker.GetByIdAsync(22, Arg.Any<CancellationToken>()).Returns(record);

        var tocPage = new StructuredPage(1, "Table of Contents", [new PageBlock(1, "body", "TOC text")]);
        _extractor.ExtractSinglePage(_tempFile, 1).Returns(tocPage);
        _extractor.ExtractPages(_tempFile).Returns([
            new StructuredPage(1, "short text", [new PageBlock(1, "body", "short text")])
        ]);
        _tocMapExtractor.ExtractMapAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TocSectionEntry>>([]));

        var expectedHash = await HashFileAsync(_tempFile);
        var sut = BuildSut();

        await sut.ExtractBookAsync(22);

        await _tracker.Received(1).MarkHashAsync(22, expectedHash, Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkExtractedAsync(22, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_PagesBelowThreshold_SkipsExtraction()
    {
        var record = MakeRecord(20, tocPage: 1);
        _tracker.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(record);

        var tocStructuredPage = new StructuredPage(1, "TOC text", [new PageBlock(1, "body", "TOC")]);
        _extractor.ExtractSinglePage(_tempFile, 1).Returns(tocStructuredPage);
        _extractor.ExtractPages(_tempFile).Returns([
            new StructuredPage(2, "short", [new PageBlock(1, "body", "short")])
        ]);
        _tocMapExtractor.ExtractMapAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TocSectionEntry>>([
                new TocSectionEntry("Spells", ContentCategory.Spell, 1, 50)
            ]));

        var sut = BuildSut();

        await sut.ExtractBookAsync(20);

        await _entityExtractor.DidNotReceive().ExtractAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkExtractedAsync(20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_TocGuidedPage_CallsExtractorWithSectionContext()
    {
        var record = MakeRecord(21, tocPage: 1);
        _tracker.GetByIdAsync(21, Arg.Any<CancellationToken>()).Returns(record);

        var tocStructuredPage = new StructuredPage(1, "TOC", [new PageBlock(1, "body", "TOC")]);
        _extractor.ExtractSinglePage(_tempFile, 1).Returns(tocStructuredPage);

        var pageText = new string('x', 200);
        _extractor.ExtractPages(_tempFile).Returns([
            new StructuredPage(45, pageText, [new PageBlock(1, "h2", "Wizard")])
        ]);

        _tocMapExtractor.ExtractMapAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TocSectionEntry>>([
                new TocSectionEntry("Wizard", ContentCategory.Class, 45, 80)
            ]));

        var entity = new ExtractedEntity(
            Page: 45, SourceBook: "PHB", Version: "Edition2014",
            Partial: false, Type: "Class", Name: "Wizard",
            Data: new JsonObject { ["description"] = "test" });

        _entityExtractor.ExtractAsync(
            Arg.Any<string>(), "Class", 45, "PHB", "Edition2014",
            "Wizard", 45, 80, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExtractedEntity>>([entity]));

        var sut = BuildSut();

        await sut.ExtractBookAsync(21);

        await _entityExtractor.Received(1).ExtractAsync(
            Arg.Any<string>(), "Class", 45, "PHB", "Edition2014",
            "Wizard", 45, 80, Arg.Any<CancellationToken>());
        await _jsonStore.Received(1).SavePageAsync(
            21, Arg.Any<StructuredPage>(), Arg.Any<IReadOnlyList<ExtractedEntity>>(),
            Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkExtractedAsync(21, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractBookAsync_PageOutsideTocMap_SkipsExtraction()
    {
        var record = MakeRecord(25, tocPage: 1);
        _tracker.GetByIdAsync(25, Arg.Any<CancellationToken>()).Returns(record);

        var tocStructuredPage = new StructuredPage(1, "TOC", [new PageBlock(1, "body", "TOC")]);
        _extractor.ExtractSinglePage(_tempFile, 1).Returns(tocStructuredPage);

        var pageText = new string('x', 200);
        _extractor.ExtractPages(_tempFile).Returns([
            new StructuredPage(999, pageText, [new PageBlock(1, "body", pageText)])
        ]);

        _tocMapExtractor.ExtractMapAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TocSectionEntry>>([
                new TocSectionEntry("Spells", ContentCategory.Spell, 45, 80)
            ]));

        var sut = BuildSut();

        await sut.ExtractBookAsync(25);

        await _entityExtractor.DidNotReceive().ExtractAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    // ── IngestJson ───────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestJsonAsync_RecordNotFound_Returns()
    {
        _tracker.GetByIdAsync(998, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);
        var sut = BuildSut();

        await sut.IngestJsonAsync(998);

        await _jsonPipeline.DidNotReceive().IngestAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestJsonAsync_ValidRecord_RunsPipelineAndMarksIngested()
    {
        var record = MakeRecord(30, IngestionStatus.Extracted);
        record.FileHash = "hash30";
        _tracker.GetByIdAsync(30, Arg.Any<CancellationToken>()).Returns(record);
        _jsonStore.LoadAllPagesAsync(30, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PageData>>([]));
        var sut = BuildSut();

        await sut.IngestJsonAsync(30);

        await _jsonPipeline.Received(1).IngestAsync(30, "hash30", Arg.Any<CancellationToken>());
        await _tracker.Received(1).MarkJsonIngestedAsync(30, 0, Arg.Any<CancellationToken>());
    }
}
```

- [ ] Run `dotnet test --filter "IngestionOrchestratorTests"` — all tests pass
- [ ] Run `dotnet test` — all tests pass

### Step 4.6: Commit

```bash
git add Features/Ingestion/Extraction/ILlmEntityExtractor.cs \
    Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs \
    Features/Ingestion/IngestionOrchestrator.cs \
    DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaLlmEntityExtractorTests.cs \
    DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs
git commit -m "feat: add section context to ExtractAsync, update orchestrator to TOC-guided section flow"
```

---

## Task 5: Registration API Changes

**Files:**
- Modify: `Features/Admin/BooksAdminEndpoints.cs`
- Modify: `DndMcpAICsharpFun.http`
- Modify: `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs`

### Step 5.1: Write failing tests for tocPage requirement and remove register-path

- [ ] Replace the test file sections in `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs`:

In `BuildClientAsync`, remove:
```csharp
        builder.Services.AddSingleton<ILogger<RegisterBookByPathRequest>>(
            NullLogger<RegisterBookByPathRequest>.Instance);
```

Add `tocPage` field to existing passing tests (`RegisterBook_ValidPdf_Returns202`) by updating the MultipartFormData content to include it:
```csharp
        content.Add(new StringContent("3"), "tocPage");
```

Add a new test for the missing-tocPage case after `RegisterBook_InvalidVersion_Returns400`:
```csharp
    [Fact]
    public async Task RegisterBook_MissingTocPage_Returns400()
    {
        var (client, _, _, _, _, _) = await BuildClientAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("PHB"), "sourceName");
        content.Add(new StringContent("Edition2014"), "version");
        content.Add(new StringContent("Player's Handbook"), "displayName");
        // tocPage intentionally omitted

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
```

Remove all tests under `// POST /admin/books/register-path` section.

Full replacement for `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Tests.Admin;

public sealed class BooksAdminEndpointsTests
{
    private static async Task<(
        HttpClient Client,
        IIngestionTracker Tracker,
        IIngestionQueue Queue,
        IIngestionOrchestrator Orchestrator,
        IEntityJsonStore JsonStore,
        IExtractionCancellationRegistry Registry)> BuildClientAsync()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var queue = Substitute.For<IIngestionQueue>();
        var orchestrator = Substitute.For<IIngestionOrchestrator>();
        var jsonStore = Substitute.For<IEntityJsonStore>();
        var registry = Substitute.For<IExtractionCancellationRegistry>();
        var pdfExtractor = Substitute.For<IPdfStructuredExtractor>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(tracker);
        builder.Services.AddSingleton(queue);
        builder.Services.AddSingleton(orchestrator);
        builder.Services.AddSingleton(jsonStore);
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(pdfExtractor);
        builder.Services.AddSingleton<ILogger<RegisterBookRequest>>(
            NullLogger<RegisterBookRequest>.Instance);
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = "test-key");
        builder.Services.Configure<IngestionOptions>(o => o.BooksPath = Path.GetTempPath());

        var app = builder.Build();
        app.MapGroup("/admin").MapBooksAdmin();

        await app.StartAsync();
        return (app.GetTestClient(), tracker, queue, orchestrator, jsonStore, registry);
    }

    private static IngestionRecord MakeRecord(
        int id = 1,
        IngestionStatus status = IngestionStatus.Pending) => new()
    {
        Id = id,
        FilePath = "/tmp/test.pdf",
        FileName = "test.pdf",
        FileHash = string.Empty,
        SourceName = "PHB",
        Version = "5e",
        DisplayName = "Player's Handbook",
        Status = status,
        TocPage = 3,
    };

    // POST /admin/books/register
    [Fact]
    public async Task RegisterBook_ValidPdf_Returns202()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
            .Returns(MakeRecord());

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("PHB"), "sourceName");
        content.Add(new StringContent("Edition2014"), "version");
        content.Add(new StringContent("Player's Handbook"), "displayName");
        content.Add(new StringContent("3"), "tocPage");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await tracker.Received(1).CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>());
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), "*_test.pdf"))
            File.Delete(f);
    }

    [Fact]
    public async Task RegisterBook_NonPdfExtension_Returns400()
    {
        var (client, _, _, _, _, _) = await BuildClientAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x00]), "file", "test.docx");
        content.Add(new StringContent("PHB"), "sourceName");
        content.Add(new StringContent("5e"), "version");
        content.Add(new StringContent("PHB"), "displayName");
        content.Add(new StringContent("3"), "tocPage");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterBook_InvalidVersion_Returns400()
    {
        var (client, _, _, _, _, _) = await BuildClientAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("PHB"), "sourceName");
        content.Add(new StringContent("invalid_version"), "version");
        content.Add(new StringContent("PHB"), "displayName");
        content.Add(new StringContent("3"), "tocPage");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterBook_MissingTocPage_Returns400()
    {
        var (client, _, _, _, _, _) = await BuildClientAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("PHB"), "sourceName");
        content.Add(new StringContent("Edition2014"), "version");
        content.Add(new StringContent("Player's Handbook"), "displayName");
        // tocPage intentionally omitted

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // GET /admin/books
    [Fact]
    public async Task GetAllBooks_Returns200WithList()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        tracker.GetAllAsync().Returns(Task.FromResult<IReadOnlyList<IngestionRecord>>([MakeRecord()]));

        var response = await client.GetAsync("/admin/books");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // POST /admin/books/{id}/extract
    [Fact]
    public async Task ExtractBook_RecordFound_Returns202()
    {
        var (client, tracker, queue, _, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(MakeRecord());

        var response = await client.PostAsync("/admin/books/1/extract", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        queue.Received(1).TryEnqueue(Arg.Any<IngestionWorkItem>());
    }

    [Fact]
    public async Task ExtractBook_RecordNotFound_Returns404()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);

        var response = await client.PostAsync("/admin/books/99/extract", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExtractBook_Processing_Returns409()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(status: IngestionStatus.Processing));

        var response = await client.PostAsync("/admin/books/1/extract", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // DELETE /admin/books/{id}
    [Fact]
    public async Task DeleteBook_Deleted_Returns204()
    {
        var (client, _, _, orchestrator, _, _) = await BuildClientAsync();
        orchestrator.DeleteBookAsync(1, Arg.Any<CancellationToken>())
            .Returns(DeleteBookResult.Deleted);

        var response = await client.DeleteAsync("/admin/books/1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBook_NotFound_Returns404()
    {
        var (client, _, _, orchestrator, _, _) = await BuildClientAsync();
        orchestrator.DeleteBookAsync(99, Arg.Any<CancellationToken>())
            .Returns(DeleteBookResult.NotFound);

        var response = await client.DeleteAsync("/admin/books/99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // POST /admin/books/{id}/extract-page/{pageNumber}
    [Fact]
    public async Task ExtractPage_UnknownBook_Returns404()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(42, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);

        var response = await client.PostAsync("/admin/books/42/extract-page/1", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] Run `dotnet test --filter "BooksAdminEndpointsTests"` — expect the `MissingTocPage` test to fail (not implemented yet)

### Step 5.2: Update BooksAdminEndpoints

- [ ] Replace `Features/Admin/BooksAdminEndpoints.cs` with the updated version (remove `RegisterBookByPath`, add `tocPage` param, remove `MapPost("/books/register-path")`):

```csharp
using System.Diagnostics.CodeAnalysis;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

public static partial class BooksAdminEndpoints
{
    public static RouteGroupBuilder MapBooksAdmin(this RouteGroupBuilder group)
    {
        group.MapPost("/books/register", RegisterBook).DisableAntiforgery();
        group.MapGet("/books", GetAllBooks);
        group.MapPost("/books/{id:int}/extract", ExtractBook).DisableAntiforgery();
        group.MapGet("/books/{id:int}/extracted", GetExtracted);
        group.MapPost("/books/{id:int}/ingest-json", IngestJson).DisableAntiforgery();
        group.MapDelete("/books/{id:int}", DeleteBook);
        group.MapPost("/books/{id:int}/cancel-extract", CancelExtract).DisableAntiforgery();
        group.MapPost("/books/{id:int}/extract-page/{pageNumber:int}", ExtractPage).DisableAntiforgery();
        return group;
    }

    private static async Task<IResult> RegisterBook(
      IFormFile file,
      [FromForm] string sourceName,
      [FromForm] string version,
      [FromForm] string displayName,
      IIngestionTracker tracker,
      IOptions<IngestionOptions> ingestionOptions,
      ILogger<RegisterBookRequest> logger,
      CancellationToken ct,
      [FromForm] int? tocPage = null)
    {
        if (tocPage is null)
            return Results.Problem("tocPage is required.", statusCode: 400);

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Results.Problem("Only PDF files are accepted.", statusCode: 400);

        if (!Enum.TryParse<DndVersion>(version, ignoreCase: true, out var parsedVersion))
            return Results.Problem(
                $"Invalid version '{version}'. Valid values: {string.Join(", ", Enum.GetNames<DndVersion>())}",
                statusCode: 400);

        var booksPath = ingestionOptions.Value.BooksPath;
        Directory.CreateDirectory(booksPath);

        var originalFileName = Path.GetFileName(file.FileName);
        var storedFileName = $"{Guid.NewGuid():N}_{originalFileName}";
        var filePath = Path.Combine(booksPath, storedFileName);

        await using (var src = file.OpenReadStream())
        await using (var dest = File.Create(filePath))
        {
            await src.CopyToAsync(dest, ct);
        }

        var record = new IngestionRecord
        {
            FilePath = filePath,
            FileName = originalFileName,
            FileHash = string.Empty,
            SourceName = sourceName,
            Version = parsedVersion.ToString(),
            DisplayName = displayName,
            Status = IngestionStatus.Pending,
            TocPage = tocPage.Value,
        };

        var created = await tracker.CreateAsync(record, ct);

        LogBookRegistered(logger, created.DisplayName, created.Id, originalFileName);

        return Results.Accepted($"/admin/books/{created.Id}", created);
    }

    private static async Task<IResult> GetAllBooks(IIngestionTracker tracker)
    {
        var records = await tracker.GetAllAsync();
        return Results.Ok(records);
    }

    private static async Task<IResult> ExtractBook(
        int id,
        IIngestionTracker tracker,
        IIngestionQueue queue,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        if (record.Status == IngestionStatus.Processing)
            return Results.Conflict("Book is currently processing. Wait before re-extracting.");

        queue.TryEnqueue(new IngestionWorkItem(IngestionWorkType.Extract, id));
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
        IIngestionQueue queue,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        if (record.Status == IngestionStatus.Processing)
            return Results.Conflict("Book is currently processing.");

        queue.TryEnqueue(new IngestionWorkItem(IngestionWorkType.IngestJson, id));
        return Results.Accepted($"/admin/books/{id}");
    }

    private static async Task<IResult> DeleteBook(
        int id,
        IIngestionOrchestrator orchestrator,
        CancellationToken ct)
    {
        var result = await orchestrator.DeleteBookAsync(id, ct);
        return result switch
        {
            DeleteBookResult.Deleted   => Results.NoContent(),
            DeleteBookResult.NotFound  => Results.NotFound(),
            DeleteBookResult.Conflict  => Results.Conflict("Book is currently being ingested. Wait for ingestion to complete before deleting."),
            _                          => Results.StatusCode(500)
        };
    }

    private static IResult CancelExtract(
        int id,
        IExtractionCancellationRegistry cancellationRegistry)
    {
        var cancelled = cancellationRegistry.Cancel(id);
        return cancelled ? Results.Ok() : Results.NotFound();
    }

    private static async Task<IResult> ExtractPage(
        int id,
        int pageNumber,
        IIngestionOrchestrator orchestrator,
        IPdfStructuredExtractor extractor,
        IIngestionTracker tracker,
        CancellationToken ct,
        [FromQuery] bool? save = null)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        using var document = UglyToad.PdfPig.PdfDocument.Open(record.FilePath);
        var totalPages = document.NumberOfPages;
        if (pageNumber < 1 || pageNumber > totalPages)
            return Results.Problem(
                $"Page {pageNumber} is out of range. Book has {totalPages} pages.",
                statusCode: 400);

        var pageData = await orchestrator.ExtractSinglePageAsync(id, pageNumber, save ?? false, ct);
        if (pageData is null)
            return Results.NotFound($"Book with id {id} not found");

        return Results.Ok(pageData);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered book {DisplayName} (id={Id}, file={File})")]
    private static partial void LogBookRegistered(ILogger logger, string displayName, int id, string file);
}

[ExcludeFromCodeCoverage]
public sealed record RegisterBookRequest(
    string SourceName,
    string Version,
    string DisplayName,
    int TocPage);
```

- [ ] Run `dotnet test --filter "BooksAdminEndpointsTests"` — all tests pass

### Step 5.3: Update DndMcpAICsharpFun.http

- [ ] Edit `DndMcpAICsharpFun.http` — add `tocPage` field to the register request and remove the `register-path` block.

In the register multipart block, add after the `displayName` part:

```
------Boundary
Content-Disposition: form-data; name="tocPage"

3
------Boundary--
```

Remove the entire `### Admin Books — Register by server-side path` block (lines 43–52 approximately).

- [ ] Run `dotnet build` — no errors

### Step 5.4: Commit

```bash
git add Features/Admin/BooksAdminEndpoints.cs \
    DndMcpAICsharpFun.http \
    DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs
git commit -m "feat: require tocPage on register, remove register-path endpoint"
```

---

## Task 6: Qdrant Payload Fields

**Files:**
- Modify: `Domain/ChunkMetadata.cs`
- Modify: `Infrastructure/Qdrant/QdrantPayloadFields.cs`
- Modify: `Infrastructure/Qdrant/QdrantCollectionInitializer.cs`
- Modify: `Features/VectorStore/QdrantVectorStoreService.cs`
- Modify: `Features/Ingestion/Extraction/JsonIngestionPipeline.cs`

### Step 6.1: Add section fields to ChunkMetadata

- [ ] Replace `Domain/ChunkMetadata.cs`:

```csharp
namespace DndMcpAICsharpFun.Domain;

public sealed record ChunkMetadata(
    string SourceBook,
    DndVersion Version,
    ContentCategory Category,
    string? EntityName,
    string Chapter,
    int PageNumber,
    int ChunkIndex,
    int? PageEnd = null,
    string? SectionTitle = null,
    int? SectionStart = null,
    int? SectionEnd = null);
```

- [ ] Run `dotnet build` — no errors

### Step 6.2: Add section constants to QdrantPayloadFields

- [ ] Replace `Infrastructure/Qdrant/QdrantPayloadFields.cs`:

```csharp
namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public static class QdrantPayloadFields
{
    public const string Text         = "text";
    public const string SourceBook   = "source_book";
    public const string Version      = "version";
    public const string Category     = "category";
    public const string EntityName   = "entity_name";
    public const string Chapter      = "chapter";
    public const string PageNumber   = "page_number";
    public const string ChunkIndex   = "chunk_index";
    public const string PageEnd      = "page_end";
    public const string SectionTitle = "section_title";
    public const string SectionStart = "section_start";
    public const string SectionEnd   = "section_end";
}
```

### Step 6.3: Add section indexes in QdrantCollectionInitializer

- [ ] Edit `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` — update `CreatePayloadIndexesAsync`:

Replace:
```csharp
        string[] keywordFields =
        [
            QdrantPayloadFields.SourceBook,
            QdrantPayloadFields.Version,
            QdrantPayloadFields.Category,
            QdrantPayloadFields.EntityName,
        ];
```

with:
```csharp
        string[] keywordFields =
        [
            QdrantPayloadFields.SourceBook,
            QdrantPayloadFields.Version,
            QdrantPayloadFields.Category,
            QdrantPayloadFields.EntityName,
            QdrantPayloadFields.SectionTitle,
        ];
```

Replace:
```csharp
        string[] intFields = [QdrantPayloadFields.PageNumber, QdrantPayloadFields.ChunkIndex, QdrantPayloadFields.PageEnd];
```

with:
```csharp
        string[] intFields =
        [
            QdrantPayloadFields.PageNumber,
            QdrantPayloadFields.ChunkIndex,
            QdrantPayloadFields.PageEnd,
            QdrantPayloadFields.SectionStart,
            QdrantPayloadFields.SectionEnd,
        ];
```

### Step 6.4: Write section fields in QdrantVectorStoreService

- [ ] Edit `Features/VectorStore/QdrantVectorStoreService.cs` — in `BuildPoint`, after the existing `PageEnd` block add:

```csharp
        if (meta.SectionTitle is not null)
            point.Payload[QdrantPayloadFields.SectionTitle] = meta.SectionTitle;

        if (meta.SectionStart.HasValue)
            point.Payload[QdrantPayloadFields.SectionStart] = (long)meta.SectionStart.Value;

        if (meta.SectionEnd.HasValue)
            point.Payload[QdrantPayloadFields.SectionEnd] = (long)meta.SectionEnd.Value;
```

### Step 6.5: Propagate section fields in JsonIngestionPipeline

- [ ] Edit `Features/Ingestion/Extraction/JsonIngestionPipeline.cs` — update `ChunkMetadata` constructor call to include section fields:

Replace:
```csharp
                var metadata = new ChunkMetadata(
                    SourceBook:  entity.SourceBook,
                    Version:     version,
                    Category:    category,
                    EntityName:  entity.Name,
                    Chapter:     string.Empty,
                    PageNumber:  entity.Page,
                    ChunkIndex:  chunkIndex++,
                    PageEnd:     entity.PageEnd);
```

with:
```csharp
                var metadata = new ChunkMetadata(
                    SourceBook:    entity.SourceBook,
                    Version:       version,
                    Category:      category,
                    EntityName:    entity.Name,
                    Chapter:       string.Empty,
                    PageNumber:    entity.Page,
                    ChunkIndex:    chunkIndex++,
                    PageEnd:       entity.PageEnd,
                    SectionTitle:  entity.SectionTitle,
                    SectionStart:  entity.SectionStart,
                    SectionEnd:    entity.SectionEnd);
```

- [ ] Run `dotnet build` — errors in `JsonIngestionPipeline` because `ExtractedEntity` doesn't have those fields yet (fixed in Task 7)

### Step 6.6: Commit (partial — will complete after Task 7)

Do not commit yet; wait until Task 7 is complete and everything builds.

---

## Task 7: ExtractedEntity Propagation

**Files:**
- Modify: `Domain/ExtractedEntity.cs`
- Modify: `Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs`
- Modify: `Features/Ingestion/Extraction/EntityJsonStore.cs`
- Modify: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/EntityJsonStoreTests.cs`
- Modify: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs`

### Step 7.1: Add section fields to ExtractedEntity

- [ ] Replace `Domain/ExtractedEntity.cs`:

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
    JsonObject Data,
    int? PageEnd = null,
    string? SectionTitle = null,
    int? SectionStart = null,
    int? SectionEnd = null);
```

- [ ] Run `dotnet build` — errors only in `OllamaLlmEntityExtractor.cs` (entity construction missing new params)

### Step 7.2: Set section fields in OllamaLlmEntityExtractor

- [ ] Edit `Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs` — in the entity construction inside `ExtractAsync`, replace:

```csharp
                    results.Add(new ExtractedEntity(pageNumber, sourceBook, version, partial, entityType, name, data));
```

with:

```csharp
                    results.Add(new ExtractedEntity(
                        pageNumber, sourceBook, version, partial, entityType, name, data,
                        PageEnd:      null,
                        SectionTitle: entityName,
                        SectionStart: sectionStartPage,
                        SectionEnd:   sectionEndPage));
```

- [ ] Run `dotnet build` — no errors

### Step 7.3: Serialize/deserialize section fields in EntityJsonStore

- [ ] Edit `Features/Ingestion/Extraction/EntityJsonStore.cs` — in `SavePageAsync`, update the entity JSON object construction.

Replace the entity `node` creation block:
```csharp
            var node = new JsonObject
            {
                ["page"]        = e.Page,
                ["source_book"] = e.SourceBook,
                ["version"]     = e.Version,
                ["partial"]     = e.Partial,
                ["type"]        = e.Type,
                ["name"]        = e.Name,
                ["data"]        = JsonNode.Parse(e.Data.ToJsonString())
            };
            if (e.PageEnd.HasValue)
                node["page_end"] = e.PageEnd.Value;
            entitiesArray.Add(node);
```

with:
```csharp
            var node = new JsonObject
            {
                ["page"]        = e.Page,
                ["source_book"] = e.SourceBook,
                ["version"]     = e.Version,
                ["partial"]     = e.Partial,
                ["type"]        = e.Type,
                ["name"]        = e.Name,
                ["data"]        = JsonNode.Parse(e.Data.ToJsonString())
            };
            if (e.PageEnd.HasValue)
                node["page_end"] = e.PageEnd.Value;
            if (e.SectionTitle is not null)
                node["section_title"] = e.SectionTitle;
            if (e.SectionStart.HasValue)
                node["section_start"] = e.SectionStart.Value;
            if (e.SectionEnd.HasValue)
                node["section_end"] = e.SectionEnd.Value;
            entitiesArray.Add(node);
```

- [ ] Edit `Features/Ingestion/Extraction/EntityJsonStore.cs` — in `ParseEntities`, update the `ExtractedEntity` construction.

Replace:
```csharp
            result.Add(new ExtractedEntity(
                Page:       o["page"]?.GetValue<int>()           ?? 0,
                SourceBook: o["source_book"]?.GetValue<string>() ?? string.Empty,
                Version:    o["version"]?.GetValue<string>()     ?? string.Empty,
                Partial:    o["partial"]?.GetValue<bool>()       ?? false,
                Type:       o["type"]?.GetValue<string>()        ?? string.Empty,
                Name:       o["name"]?.GetValue<string>()        ?? string.Empty,
                Data:       data,
                PageEnd:    pageEnd));
```

with:
```csharp
            int? sectionStart = null;
            if (o["section_start"] is JsonValue ssVal && ssVal.TryGetValue<int>(out var ssInt))
                sectionStart = ssInt;
            int? sectionEnd = null;
            if (o["section_end"] is JsonValue seVal && seVal.TryGetValue<int>(out var seInt))
                sectionEnd = seInt;

            result.Add(new ExtractedEntity(
                Page:         o["page"]?.GetValue<int>()           ?? 0,
                SourceBook:   o["source_book"]?.GetValue<string>() ?? string.Empty,
                Version:      o["version"]?.GetValue<string>()     ?? string.Empty,
                Partial:      o["partial"]?.GetValue<bool>()       ?? false,
                Type:         o["type"]?.GetValue<string>()        ?? string.Empty,
                Name:         o["name"]?.GetValue<string>()        ?? string.Empty,
                Data:         data,
                PageEnd:      pageEnd,
                SectionTitle: o["section_title"]?.GetValue<string>(),
                SectionStart: sectionStart,
                SectionEnd:   sectionEnd));
```

- [ ] Run `dotnet build` — no errors

### Step 7.4: Write tests for section field round-trip in EntityJsonStore

- [ ] Add to `DndMcpAICsharpFun.Tests/Ingestion/Extraction/EntityJsonStoreTests.cs` (append after existing tests):

```csharp
    [Fact]
    public async Task SaveAndLoad_SectionFields_RoundtripCorrectly()
    {
        var page = MakePage(10, "section text");
        var entity = new ExtractedEntity(
            Page: 10, SourceBook: "PHB", Version: "Edition2014",
            Partial: false, Type: "Class", Name: "Wizard",
            Data: new JsonObject { ["description"] = "arcane mage" },
            PageEnd: null,
            SectionTitle: "Wizard",
            SectionStart: 112,
            SectionEnd: 121);

        await _store.SavePageAsync(bookId: 77, page, [entity]);
        var pages = await _store.LoadAllPagesAsync(77);

        Assert.Single(pages);
        var loaded = pages[0].Entities[0];
        Assert.Equal("Wizard", loaded.SectionTitle);
        Assert.Equal(112, loaded.SectionStart);
        Assert.Equal(121, loaded.SectionEnd);
    }

    [Fact]
    public async Task SaveAndLoad_NullSectionFields_RoundtripAsNull()
    {
        var page = MakePage(11);
        var entity = new ExtractedEntity(
            Page: 11, SourceBook: "PHB", Version: "Edition2014",
            Partial: false, Type: "Rule", Name: "Proficiency",
            Data: new JsonObject { ["description"] = "rules text" });

        await _store.SavePageAsync(bookId: 88, page, [entity]);
        var pages = await _store.LoadAllPagesAsync(88);

        var loaded = pages[0].Entities[0];
        Assert.Null(loaded.SectionTitle);
        Assert.Null(loaded.SectionStart);
        Assert.Null(loaded.SectionEnd);
    }
```

### Step 7.5: Write test for section propagation in JsonIngestionPipeline

- [ ] Add to `DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs` (append after existing tests):

```csharp
    [Fact]
    public async Task IngestAsync_PropagatesSectionFieldsToChunkMetadata()
    {
        var entity = new ExtractedEntity(
            1, "PHB", "Edition2014", false, "Class", "Wizard",
            new JsonObject { ["description"] = "arcane mage" },
            PageEnd: null,
            SectionTitle: "Wizard",
            SectionStart: 112,
            SectionEnd: 121);

        _store.LoadAllPagesAsync(42).Returns(
            Task.FromResult<IReadOnlyList<PageData>>(
                [new PageData(1, string.Empty, [], [entity])]));

        await _pipeline.IngestAsync(bookId: 42, fileHash: "abc123");

        await _ingestor.Received(1).IngestAsync(
            Arg.Is<IList<ContentChunk>>(chunks =>
                chunks.Count == 1 &&
                chunks[0].Metadata.SectionTitle == "Wizard" &&
                chunks[0].Metadata.SectionStart == 112 &&
                chunks[0].Metadata.SectionEnd == 121),
            "abc123",
            Arg.Any<CancellationToken>());
    }
```

- [ ] Run `dotnet test --filter "EntityJsonStoreTests|JsonIngestionPipelineTests"` — all tests pass
- [ ] Run `dotnet test` — all tests pass

### Step 7.6: Commit all Task 6 and Task 7 changes

```bash
git add Domain/ExtractedEntity.cs \
    Domain/ChunkMetadata.cs \
    Infrastructure/Qdrant/QdrantPayloadFields.cs \
    Infrastructure/Qdrant/QdrantCollectionInitializer.cs \
    Features/VectorStore/QdrantVectorStoreService.cs \
    Features/Ingestion/Extraction/JsonIngestionPipeline.cs \
    Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs \
    Features/Ingestion/Extraction/EntityJsonStore.cs \
    DndMcpAICsharpFun.Tests/Ingestion/Extraction/EntityJsonStoreTests.cs \
    DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs
git commit -m "feat: propagate SectionTitle/Start/End through ExtractedEntity, EntityJsonStore, ChunkMetadata, Qdrant payload"
```

---

## Final Verification

- [ ] Run `dotnet build` — no errors, no warnings about unused references
- [ ] Run `dotnet test` — all tests pass

### Final commit (if there are any uncommitted changes)

```bash
git status
# commit anything remaining
```
