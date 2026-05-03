# Structured PDF Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace flat Y-coordinate PDF text extraction with DocstrumBoundingBoxes + reading-order segmentation, enrich per-page JSON with structured blocks, track multi-page entity ranges, add a single-page test endpoint, and upgrade the embedding model to mxbai-embed-large.

**Architecture:** A new `IPdfStructuredExtractor` / `PdfPigStructuredExtractor` replaces `IPdfTextExtractor`. The orchestrator formats structured blocks as `[H2]/[H3]/body` text before passing to the LLM. Per-page JSON changes from a bare entity array to `{ page, raw_text, blocks, entities }`. The merge pass tracks `PageEnd` on multi-page entities; Qdrant stores it as payload. Config-only change for embedding model upgrade.

**Tech Stack:** UglyToad.PdfPig (already referenced — DocstrumBoundingBoxes + UnsupervisedReadingOrderDetector are in the existing package), ASP.NET Core minimal APIs, xUnit + NSubstitute, Qdrant.Client.

---

## File Map

**New files:**
- `Domain/PageBlock.cs` — `record PageBlock(int Order, string Level, string Text)`
- `Domain/StructuredPage.cs` — `record StructuredPage(int PageNumber, string RawText, IReadOnlyList<PageBlock> Blocks)`
- `Domain/PageData.cs` — combines structured page + extracted entities for storage
- `Features/Ingestion/Pdf/IPdfStructuredExtractor.cs` — interface
- `Features/Ingestion/Pdf/PdfPigStructuredExtractor.cs` — DocstrumBoundingBoxes implementation
- `DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigStructuredExtractorTests.cs`

**Modified files:**
- `Domain/ExtractedEntity.cs` — add `int? PageEnd = null`
- `Domain/ChunkMetadata.cs` — add `int? PageEnd = null`
- `Features/Ingestion/Extraction/IEntityJsonStore.cs` — new save/load signatures
- `Features/Ingestion/Extraction/EntityJsonStore.cs` — enriched JSON format
- `Features/Ingestion/Extraction/JsonIngestionPipeline.cs` — read PageData, propagate PageEnd
- `Features/Ingestion/IIngestionOrchestrator.cs` — add ExtractSinglePageAsync
- `Features/Ingestion/IngestionOrchestrator.cs` — wire structured extractor, BuildPromptText, new method
- `Features/Admin/BooksAdminEndpoints.cs` — new extract-page endpoint
- `Infrastructure/Qdrant/QdrantPayloadFields.cs` — add PageEnd constant
- `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` — add page_end index
- `Features/VectorStore/QdrantVectorStoreService.cs` — write page_end payload
- `Extensions/ServiceCollectionExtensions.cs` — swap IPdfTextExtractor for IPdfStructuredExtractor
- `Config/appsettings.json` — mxbai-embed-large, VectorSize 1024
- `docker-compose.yml` — pull mxbai-embed-large, add volume wipe comment
- `DndMcpAICsharpFun.http` — add extract-page example
- `DndMcpAICsharpFun.Tests/Ingestion/Extraction/EntityJsonStoreTests.cs` — update for new format
- `DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs` — update for PageData
- `DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs` — swap extractor mock

**Deleted files:**
- `Features/Ingestion/Pdf/IPdfTextExtractor.cs`
- `Features/Ingestion/Pdf/PdfPigTextExtractor.cs`

---

## Task 1: Domain records

**Files:**
- Create: `Domain/PageBlock.cs`
- Create: `Domain/StructuredPage.cs`
- Create: `Domain/PageData.cs`
- Modify: `Domain/ExtractedEntity.cs`
- Modify: `Domain/ChunkMetadata.cs`

- [ ] **Step 1.1: Create PageBlock**

```csharp
// Domain/PageBlock.cs
namespace DndMcpAICsharpFun.Domain;

public sealed record PageBlock(int Order, string Level, string Text);
```

- [ ] **Step 1.2: Create StructuredPage**

```csharp
// Domain/StructuredPage.cs
namespace DndMcpAICsharpFun.Domain;

public sealed record StructuredPage(
    int PageNumber,
    string RawText,
    IReadOnlyList<PageBlock> Blocks);
```

- [ ] **Step 1.3: Create PageData**

```csharp
// Domain/PageData.cs
namespace DndMcpAICsharpFun.Domain;

public sealed record PageData(
    int PageNumber,
    string RawText,
    IReadOnlyList<PageBlock> Blocks,
    IReadOnlyList<ExtractedEntity> Entities);
```

- [ ] **Step 1.4: Add PageEnd to ExtractedEntity**

Replace `Domain/ExtractedEntity.cs` content:

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
    int? PageEnd = null);
```

- [ ] **Step 1.5: Add PageEnd to ChunkMetadata**

Replace `Domain/ChunkMetadata.cs` content:

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
    int? PageEnd = null);
```

- [ ] **Step 1.6: Build and verify no compile errors**

```bash
dotnet build DndMcpAICsharpFun.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 1.7: Commit**

```bash
git add Domain/PageBlock.cs Domain/StructuredPage.cs Domain/PageData.cs Domain/ExtractedEntity.cs Domain/ChunkMetadata.cs
git commit -m "feat: add PageBlock, StructuredPage, PageData domain records; add PageEnd to ExtractedEntity and ChunkMetadata"
```

---

## Task 2: IPdfStructuredExtractor + heading inference unit tests

**Files:**
- Create: `Features/Ingestion/Pdf/IPdfStructuredExtractor.cs`
- Create: `Features/Ingestion/Pdf/PdfPigStructuredExtractor.cs`
- Create: `DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigStructuredExtractorTests.cs`

- [ ] **Step 2.1: Create IPdfStructuredExtractor**

```csharp
// Features/Ingestion/Pdf/IPdfStructuredExtractor.cs
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public interface IPdfStructuredExtractor
{
    IEnumerable<StructuredPage> ExtractPages(string filePath);
    StructuredPage? ExtractSinglePage(string filePath, int pageNumber);
}
```

- [ ] **Step 2.2: Write failing heading-inference unit test**

Create `DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigStructuredExtractorTests.cs`:

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class PdfPigStructuredExtractorTests
{
    private static PdfPigStructuredExtractor BuildSut(int minChars = 0) =>
        new(Options.Create(new IngestionOptions { MinPageCharacters = minChars }),
            NullLogger<PdfPigStructuredExtractor>.Instance);

    [Fact]
    public void InferHeadingLevels_ThreeDistinctSizes_AssignsH1H2Body()
    {
        var blocks = new[]
        {
            (FontSize: 18.0, Text: "Big Heading"),
            (FontSize: 14.0, Text: "Sub Heading"),
            (FontSize: 10.0, Text: "Body text here"),
            (FontSize: 10.0, Text: "More body text"),
        };

        var result = PdfPigStructuredExtractor.InferHeadingLevels(blocks);

        Assert.Equal(4, result.Count);
        Assert.Equal("h1",   result[0].Level);
        Assert.Equal("h2",   result[1].Level);
        Assert.Equal("body", result[2].Level);
        Assert.Equal("body", result[3].Level);
    }

    [Fact]
    public void InferHeadingLevels_SingleFontSize_AllBody()
    {
        var blocks = new[]
        {
            (FontSize: 12.0, Text: "Line one"),
            (FontSize: 12.0, Text: "Line two"),
        };

        var result = PdfPigStructuredExtractor.InferHeadingLevels(blocks);

        Assert.All(result, b => Assert.Equal("body", b.Level));
    }

    [Fact]
    public void InferHeadingLevels_FourDistinctSizes_AssignsH1H2H3Body()
    {
        var blocks = new[]
        {
            (FontSize: 24.0, Text: "Chapter"),
            (FontSize: 18.0, Text: "Section"),
            (FontSize: 14.0, Text: "Sub-section"),
            (FontSize: 10.0, Text: "Paragraph"),
        };

        var result = PdfPigStructuredExtractor.InferHeadingLevels(blocks);

        Assert.Equal("h1",   result[0].Level);
        Assert.Equal("h2",   result[1].Level);
        Assert.Equal("h3",   result[2].Level);
        Assert.Equal("body", result[3].Level);
    }

    [Fact]
    public void InferHeadingLevels_EmptyInput_ReturnsEmpty()
    {
        var result = PdfPigStructuredExtractor.InferHeadingLevels([]);
        Assert.Empty(result);
    }

    [Fact]
    public void InferHeadingLevels_OrderFieldMatchesPosition()
    {
        var blocks = new[]
        {
            (FontSize: 12.0, Text: "A"),
            (FontSize: 12.0, Text: "B"),
            (FontSize: 12.0, Text: "C"),
        };

        var result = PdfPigStructuredExtractor.InferHeadingLevels(blocks);

        Assert.Equal([1, 2, 3], result.Select(b => b.Order));
    }
}
```

- [ ] **Step 2.3: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "PdfPigStructuredExtractorTests"
```

Expected: FAIL — `PdfPigStructuredExtractor` does not exist yet.

- [ ] **Step 2.4: Implement PdfPigStructuredExtractor**

```csharp
// Features/Ingestion/Pdf/PdfPigStructuredExtractor.cs
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrder;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed partial class PdfPigStructuredExtractor(
    IOptions<IngestionOptions> options,
    ILogger<PdfPigStructuredExtractor> logger) : IPdfStructuredExtractor
{
    private readonly int _minPageCharacters = options.Value.MinPageCharacters;

    public IEnumerable<StructuredPage> ExtractPages(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
            yield return ExtractPage(page);
    }

    public StructuredPage? ExtractSinglePage(string filePath, int pageNumber)
    {
        using var document = PdfDocument.Open(filePath);
        var page = document.GetPages().FirstOrDefault(p => p.Number == pageNumber);
        if (page is null) return null;
        return ExtractPage(page);
    }

    private StructuredPage ExtractPage(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0)
        {
            if (0 < _minPageCharacters)
                LogSparsePage(logger, page.Number, 0);
            return new StructuredPage(page.Number, string.Empty, []);
        }

        var textBlocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        var orderedBlocks = UnsupervisedReadingOrderDetector.Instance.Get(textBlocks).ToList();

        var blockData = orderedBlocks
            .Select(static b =>
            {
                var letters = b.Words.SelectMany(static w => w.Letters).ToList();
                var sizes = letters.Select(static l => l.FontSize).Order().ToList();
                var median = sizes.Count > 0 ? sizes[sizes.Count / 2] : 0.0;
                return (FontSize: median, Text: b.Text);
            })
            .ToArray();

        var blocks = InferHeadingLevels(blockData);
        var rawText = string.Join("\n", blocks.Select(static b => b.Text));

        if (rawText.Length < _minPageCharacters)
            LogSparsePage(logger, page.Number, rawText.Length);

        return new StructuredPage(page.Number, rawText, blocks);
    }

    internal static IReadOnlyList<PageBlock> InferHeadingLevels(
        IReadOnlyList<(double FontSize, string Text)> blockData)
    {
        if (blockData.Count == 0) return [];

        var distinctSizes = blockData
            .Select(static b => b.FontSize)
            .Where(static s => s > 0)
            .Distinct()
            .OrderDescending()
            .ToList();

        string GetLevel(double size) => distinctSizes.Count switch
        {
            0 => "body",
            1 => "body",
            _ when size >= distinctSizes[0] => "h1",
            _ when distinctSizes.Count > 1 && size >= distinctSizes[1] => "h2",
            _ when distinctSizes.Count > 2 && size >= distinctSizes[2] => "h3",
            _ => "body"
        };

        return blockData
            .Select((b, i) => new PageBlock(i + 1, GetLevel(b.FontSize), b.Text))
            .ToList();
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Sparse page {Page} ({Chars} chars)")]
    private static partial void LogSparsePage(ILogger logger, int page, int chars);
}
```

- [ ] **Step 2.5: Run heading tests to verify they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "PdfPigStructuredExtractorTests"
```

Expected: all 5 pass.

- [ ] **Step 2.6: Add PDF integration tests**

Append to `DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigStructuredExtractorTests.cs`:

```csharp
    // Integration tests — require PdfDocumentBuilder
    [Fact]
    public void ExtractPages_SinglePage_ReturnsOneStructuredPage()
    {
        var path = BuildTempPdf(b =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            var page = b.AddPage(PageSize.A4);
            page.AddText("Fireball", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        });
        try
        {
            var sut = BuildSut();
            var result = sut.ExtractPages(path).ToList();
            Assert.Single(result);
            Assert.Equal(1, result[0].PageNumber);
            Assert.Contains("Fireball", result[0].RawText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExtractSinglePage_ValidPage_ReturnsStructuredPage()
    {
        var path = BuildTempPdf(b =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            b.AddPage(PageSize.A4); // page 1 - empty
            var p2 = b.AddPage(PageSize.A4);
            p2.AddText("Spell", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        });
        try
        {
            var sut = BuildSut();
            var result = sut.ExtractSinglePage(path, 2);
            Assert.NotNull(result);
            Assert.Equal(2, result!.PageNumber);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExtractSinglePage_PageOutOfRange_ReturnsNull()
    {
        var path = BuildTempPdf(b => b.AddPage(PageSize.A4));
        try
        {
            var result = BuildSut().ExtractSinglePage(path, 99);
            Assert.Null(result);
        }
        finally { File.Delete(path); }
    }

    private static string BuildTempPdf(Action<PdfDocumentBuilder> configure)
    {
        var builder = new PdfDocumentBuilder();
        configure(builder);
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, builder.Build());
        return path;
    }
```

Add usings to the top of the file:

```csharp
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
```

- [ ] **Step 2.7: Run all structured extractor tests**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "PdfPigStructuredExtractorTests"
```

Expected: all 8 pass.

- [ ] **Step 2.8: Commit**

```bash
git add Features/Ingestion/Pdf/IPdfStructuredExtractor.cs Features/Ingestion/Pdf/PdfPigStructuredExtractor.cs DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigStructuredExtractorTests.cs
git commit -m "feat: add IPdfStructuredExtractor and PdfPigStructuredExtractor with heading inference"
```

---

## Task 3: Enriched EntityJsonStore

**Files:**
- Modify: `Features/Ingestion/Extraction/IEntityJsonStore.cs`
- Modify: `Features/Ingestion/Extraction/EntityJsonStore.cs`
- Modify: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/EntityJsonStoreTests.cs`

- [ ] **Step 3.1: Update IEntityJsonStore**

Replace `Features/Ingestion/Extraction/IEntityJsonStore.cs`:

```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface IEntityJsonStore
{
    Task SavePageAsync(int bookId, StructuredPage page, IReadOnlyList<ExtractedEntity> entities, CancellationToken ct = default);
    Task<IReadOnlyList<PageData>> LoadAllPagesAsync(int bookId, CancellationToken ct = default);
    Task RunMergePassAsync(int bookId, CancellationToken ct = default);
    IEnumerable<string> ListPageFiles(int bookId);
    void DeleteAllPages(int bookId);
}
```

- [ ] **Step 3.2: Write failing EntityJsonStore tests for new format**

Replace the full content of `DndMcpAICsharpFun.Tests/Ingestion/Extraction/EntityJsonStoreTests.cs`:

```csharp
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
        _store = new EntityJsonStore(Options.Create(new IngestionOptions { BooksPath = _tempRoot }));
    }

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    private static StructuredPage MakePage(int pageNumber, string text = "text") =>
        new(pageNumber, text, [new PageBlock(1, "body", text)]);

    private static ExtractedEntity MakeEntity(string name, int page, bool partial = false) =>
        new(page, "PHB", "Edition2014", partial, "Rule", name,
            new JsonObject { ["description"] = name });

    [Fact]
    public async Task SaveAndLoadRoundtrip()
    {
        var page = MakePage(1, "Big fire.");
        var entity = new ExtractedEntity(1, "PHB", "Edition2014", false, "Spell", "Fireball",
            new JsonObject { ["level"] = 3, ["description"] = "Big fire." });

        await _store.SavePageAsync(bookId: 99, page, [entity]);
        var pages = await _store.LoadAllPagesAsync(99);

        Assert.Single(pages);
        Assert.Single(pages[0].Entities);
        var loaded = pages[0].Entities[0];
        Assert.Equal("Fireball", loaded.Name);
        Assert.Equal(3, loaded.Data["level"]?.GetValue<int>());
        Assert.Equal("Big fire.", pages[0].RawText);
        Assert.Single(pages[0].Blocks);
    }

    [Fact]
    public async Task LoadAllPagesReturnsInPageOrder()
    {
        await _store.SavePageAsync(99, MakePage(3), [MakeEntity("C", 3)]);
        await _store.SavePageAsync(99, MakePage(1), [MakeEntity("A", 1)]);
        await _store.SavePageAsync(99, MakePage(2), [MakeEntity("B", 2)]);

        var pages = await _store.LoadAllPagesAsync(99);

        Assert.Equal(3, pages.Count);
        Assert.Equal("A", pages[0].Entities[0].Name);
        Assert.Equal("B", pages[1].Entities[0].Name);
        Assert.Equal("C", pages[2].Entities[0].Name);
    }

    [Fact]
    public async Task MergePassConcatenatesDescriptionAndDropsDuplicate()
    {
        var partial = new ExtractedEntity(1, "PHB", "Edition2014", true, "Spell", "Fireball",
            new JsonObject { ["description"] = "Big fire" });
        var continuation = new ExtractedEntity(2, "PHB", "Edition2014", false, "Spell", "Fireball",
            new JsonObject { ["description"] = " ball." });

        await _store.SavePageAsync(99, MakePage(1, "Big fire"), [partial]);
        await _store.SavePageAsync(99, MakePage(2, " ball."), [continuation]);

        await _store.RunMergePassAsync(99);

        var pages = await _store.LoadAllPagesAsync(99);
        Assert.Single(pages[0].Entities);
        Assert.Empty(pages[1].Entities);
        Assert.Equal("Big fire ball.", pages[0].Entities[0].Data["description"]?.GetValue<string>());
        Assert.False(pages[0].Entities[0].Partial);
    }

    [Fact]
    public async Task MergePassSetsPageEndOnMergedEntity()
    {
        var partial = new ExtractedEntity(1, "PHB", "Edition2014", true, "Class", "Halfling",
            new JsonObject { ["description"] = "First part" });
        var continuation = new ExtractedEntity(2, "PHB", "Edition2014", false, "Class", "Halfling",
            new JsonObject { ["description"] = " second part." });

        await _store.SavePageAsync(99, MakePage(1), [partial]);
        await _store.SavePageAsync(99, MakePage(2), [continuation]);

        await _store.RunMergePassAsync(99);

        var pages = await _store.LoadAllPagesAsync(99);
        Assert.Equal(2, pages[0].Entities[0].PageEnd);
    }

    [Fact]
    public async Task MergePassSinglePageEntityHasNullPageEnd()
    {
        var entity = MakeEntity("Dragon", 1);
        await _store.SavePageAsync(99, MakePage(1), [entity]);

        await _store.RunMergePassAsync(99);

        var pages = await _store.LoadAllPagesAsync(99);
        Assert.Null(pages[0].Entities[0].PageEnd);
    }

    [Fact]
    public async Task OldBareArrayFormatReturnsEmptyEntities()
    {
        var dir = Path.Combine(_tempRoot, "extracted", "99");
        Directory.CreateDirectory(dir);
        // Write old bare-array format
        await File.WriteAllTextAsync(Path.Combine(dir, "page_1.json"),
            """[{"page":1,"source_book":"PHB","version":"Edition2014","partial":false,"type":"Rule","name":"Test","data":{}}]""");

        var pages = await _store.LoadAllPagesAsync(99);

        Assert.Single(pages);
        Assert.Empty(pages[0].Entities);
    }

    [Fact]
    public void ListPageFilesReturnsAllFiles()
    {
        var dir = Path.Combine(_tempRoot, "extracted", "99");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "page_1.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "page_2.json"), "{}");

        Assert.Equal(2, _store.ListPageFiles(99).Count());
    }

    [Fact]
    public void DeleteAllPagesRemovesDirectory()
    {
        var dir = Path.Combine(_tempRoot, "extracted", "99");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "page_1.json"), "{}");

        _store.DeleteAllPages(99);

        Assert.False(Directory.Exists(dir));
    }
}
```

- [ ] **Step 3.3: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "EntityJsonStoreTests"
```

Expected: compile errors or failures.

- [ ] **Step 3.4: Rewrite EntityJsonStore**

Replace `Features/Ingestion/Extraction/EntityJsonStore.cs`:

```csharp
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

    public async Task SavePageAsync(
        int bookId, StructuredPage page, IReadOnlyList<ExtractedEntity> entities,
        CancellationToken ct = default)
    {
        var dir = ExtractedDir(bookId);
        Directory.CreateDirectory(dir);

        var blocksArray = new JsonArray();
        foreach (var b in page.Blocks)
            blocksArray.Add(new JsonObject
            {
                ["order"] = b.Order,
                ["level"] = b.Level,
                ["text"]  = b.Text
            });

        var entitiesArray = new JsonArray();
        foreach (var e in entities)
        {
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
        }

        var root = new JsonObject
        {
            ["page"]      = page.PageNumber,
            ["raw_text"]  = page.RawText,
            ["blocks"]    = blocksArray,
            ["entities"]  = entitiesArray
        };

        await File.WriteAllTextAsync(PageFile(dir, page.PageNumber), root.ToJsonString(JsonOpts), ct);
    }

    public async Task<IReadOnlyList<PageData>> LoadAllPagesAsync(int bookId, CancellationToken ct = default)
    {
        var dir = ExtractedDir(bookId);
        if (!Directory.Exists(dir)) return [];

        var files = Directory.GetFiles(dir, "page_*.json")
            .OrderBy(ExtractPageNumber)
            .ToList();

        var result = new List<PageData>(files.Count);
        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file, ct);
            result.Add(ParsePageFile(json, ExtractPageNumber(file)));
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

        var pages = new List<(StructuredPage Page, List<ExtractedEntity> Entities)>();
        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var data = ParsePageFile(json, ExtractPageNumber(file));
            pages.Add((new StructuredPage(data.PageNumber, data.RawText, data.Blocks), [.. data.Entities]));
        }

        for (int i = 0; i < pages.Count - 1; i++)
        {
            var (_, current) = pages[i];
            var (_, next) = pages[i + 1];

            foreach (var entity in current.Where(static e => e.Partial).ToList())
            {
                var match = next.FirstOrDefault(e =>
                    string.Equals(e.Type, entity.Type, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Name, entity.Name, StringComparison.OrdinalIgnoreCase));

                if (match is null) continue;

                var thisDesc = entity.Data["description"]?.GetValue<string>() ?? string.Empty;
                var nextDesc = match.Data["description"]?.GetValue<string>() ?? string.Empty;
                var mergedData = JsonNode.Parse(entity.Data.ToJsonString())!.AsObject();
                mergedData["description"] = thisDesc + nextDesc;

                var pageEnd = match.PageEnd ?? match.Page;
                var merged = entity with { Partial = false, Data = mergedData, PageEnd = pageEnd };
                current[current.IndexOf(entity)] = merged;
                next.Remove(match);
            }
        }

        for (int i = 0; i < files.Count; i++)
        {
            var (page, entities) = pages[i];
            await SavePageAsync(bookId, page, entities, ct);
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

    private static PageData ParsePageFile(string json, int fallbackPageNumber)
    {
        var root = JsonNode.Parse(json);

        // New enriched format: { page, raw_text, blocks, entities }
        if (root is JsonObject obj && obj.ContainsKey("entities"))
        {
            var pageNumber = obj["page"]?.GetValue<int>() ?? fallbackPageNumber;
            var rawText    = obj["raw_text"]?.GetValue<string>() ?? string.Empty;
            var blocks     = ParseBlocks(obj["blocks"]?.AsArray());
            var entities   = ParseEntities(obj["entities"]?.AsArray());
            return new PageData(pageNumber, rawText, blocks, entities);
        }

        // Old bare-array format — not supported, return empty entities
        return new PageData(fallbackPageNumber, string.Empty, [], []);
    }

    private static IReadOnlyList<PageBlock> ParseBlocks(JsonArray? arr)
    {
        if (arr is null) return [];
        var result = new List<PageBlock>(arr.Count);
        foreach (var node in arr)
        {
            if (node is not JsonObject o) continue;
            result.Add(new PageBlock(
                o["order"]?.GetValue<int>() ?? 0,
                o["level"]?.GetValue<string>() ?? "body",
                o["text"]?.GetValue<string>()  ?? string.Empty));
        }
        return result;
    }

    private static IReadOnlyList<ExtractedEntity> ParseEntities(JsonArray? arr)
    {
        if (arr is null) return [];
        var result = new List<ExtractedEntity>(arr.Count);
        foreach (var node in arr)
        {
            if (node is not JsonObject o) continue;
            var data   = o["data"]?.AsObject() ?? new JsonObject();
            var pageEnd = o.ContainsKey("page_end") ? o["page_end"]?.GetValue<int>() : null;
            result.Add(new ExtractedEntity(
                Page:       o["page"]?.GetValue<int>()        ?? 0,
                SourceBook: o["source_book"]?.GetValue<string>() ?? string.Empty,
                Version:    o["version"]?.GetValue<string>()     ?? string.Empty,
                Partial:    o["partial"]?.GetValue<bool>()       ?? false,
                Type:       o["type"]?.GetValue<string>()        ?? string.Empty,
                Name:       o["name"]?.GetValue<string>()        ?? string.Empty,
                Data:       data,
                PageEnd:    pageEnd));
        }
        return result;
    }

    private static int ExtractPageNumber(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        return int.TryParse(name.AsSpan(5), out var n) ? n : 0;
    }
}
```

- [ ] **Step 3.5: Run EntityJsonStore tests**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "EntityJsonStoreTests"
```

Expected: all 8 pass.

- [ ] **Step 3.6: Commit**

```bash
git add Features/Ingestion/Extraction/IEntityJsonStore.cs Features/Ingestion/Extraction/EntityJsonStore.cs DndMcpAICsharpFun.Tests/Ingestion/Extraction/EntityJsonStoreTests.cs
git commit -m "feat: enrich EntityJsonStore with structured blocks + PageEnd tracking in merge pass"
```

---

## Task 4: JsonIngestionPipeline — use PageData + propagate PageEnd

**Files:**
- Modify: `Features/Ingestion/Extraction/JsonIngestionPipeline.cs`
- Modify: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs`

- [ ] **Step 4.1: Read existing JsonIngestionPipelineTests to understand mocks**

Read `DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs` — note how `IEntityJsonStore` is mocked; the mock must now return `IReadOnlyList<PageData>` instead of `IReadOnlyList<IReadOnlyList<ExtractedEntity>>`.

- [ ] **Step 4.2: Update JsonIngestionPipeline**

Replace `Features/Ingestion/Extraction/JsonIngestionPipeline.cs`:

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;

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
            foreach (var entity in page.Entities)
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
                    ChunkIndex:  chunkIndex++,
                    PageEnd:     entity.PageEnd);

                chunks.Add(new ContentChunk(description, metadata));
            }
        }

        await embeddingIngestor.IngestAsync(chunks, fileHash, ct);
    }
}
```

- [ ] **Step 4.3: Update JsonIngestionPipelineTests mocks**

Open `DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs`. Replace every `IReadOnlyList<IReadOnlyList<ExtractedEntity>>` mock return with `IReadOnlyList<PageData>`. For example, a mock returning entities on page 1:

```csharp
_jsonStore.LoadAllPagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
    .Returns(new List<PageData>
    {
        new PageData(1, "text",
            [new PageBlock(1, "body", "text")],
            [new ExtractedEntity(1, "PHB", "Edition2014", false, "Spell", "Fireball",
                new JsonObject { ["description"] = "A burst of fire." })])
    });
```

Also update the chunk count assertions in `IngestJsonAsync` — it now uses `page.Entities` instead of the flat list. The logic is the same; only the access path changes.

- [ ] **Step 4.4: Run pipeline tests**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "JsonIngestionPipelineTests"
```

Expected: all pass.

- [ ] **Step 4.5: Commit**

```bash
git add Features/Ingestion/Extraction/JsonIngestionPipeline.cs DndMcpAICsharpFun.Tests/Ingestion/Extraction/JsonIngestionPipelineTests.cs
git commit -m "feat: JsonIngestionPipeline reads PageData and propagates PageEnd into ChunkMetadata"
```

---

## Task 5: IngestionOrchestrator — wire structured extractor

**Files:**
- Modify: `Features/Ingestion/IngestionOrchestrator.cs`
- Modify: `Features/Ingestion/IIngestionOrchestrator.cs`
- Modify: `DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs`

- [ ] **Step 5.1: Add ExtractSinglePageAsync to IIngestionOrchestrator**

Append to `Features/Ingestion/IIngestionOrchestrator.cs`:

```csharp
using DndMcpAICsharpFun.Domain;

// Inside the interface:
Task<PageData?> ExtractSinglePageAsync(int bookId, int pageNumber, bool save, CancellationToken ct = default);
```

Full file after edit:

```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion;

public interface IIngestionOrchestrator
{
    Task ExtractBookAsync(int recordId, CancellationToken cancellationToken = default);
    Task IngestJsonAsync(int recordId, CancellationToken cancellationToken = default);
    Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken cancellationToken = default);
    Task<PageData?> ExtractSinglePageAsync(int bookId, int pageNumber, bool save, CancellationToken ct = default);
}
```

- [ ] **Step 5.2: Update IngestionOrchestrator constructor and ExtractBookAsync**

In `Features/Ingestion/IngestionOrchestrator.cs`:

1. Replace `IPdfTextExtractor extractor` constructor parameter with `IPdfStructuredExtractor extractor`.
2. Replace the `ExtractBookAsync` page loop. The loop currently calls `extractor.ExtractPages(record.FilePath)` returning `(int PageNumber, string Text)` tuples. Replace with `StructuredPage` iteration and formatted prompt text.

Here is the updated section (replace lines 12–127 of the existing file):

```csharp
public sealed partial class IngestionOrchestrator(
    IIngestionTracker tracker,
    IPdfStructuredExtractor extractor,        // changed from IPdfTextExtractor
    IVectorStoreService vectorStore,
    ILlmClassifier classifier,
    ILlmEntityExtractor entityExtractor,
    IEntityJsonStore jsonStore,
    IJsonIngestionPipeline jsonPipeline,
    IOptions<IngestionOptions> ingestionOptions,
    IPdfBookmarkReader bookmarkReader,
    ITocCategoryClassifier tocClassifier,
    ILogger<IngestionOrchestrator> logger) : IIngestionOrchestrator
{
    public async Task ExtractBookAsync(int recordId, CancellationToken cancellationToken = default)
    {
        var record = await tracker.GetByIdAsync(recordId, cancellationToken);
        if (record is null) { LogRecordNotFound(logger, recordId); return; }

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

            var bookmarks = bookmarkReader.ReadBookmarks(record.FilePath);
            LogClassifyingToc(logger, bookmarks.Count, recordId);
            var tocMap = await tocClassifier.ClassifyAsync(bookmarks, cancellationToken);

            if (tocMap.IsEmpty) LogTocFallback(logger, record.DisplayName, recordId);
            else                LogTocGuided(logger, record.DisplayName, recordId);

            var pages = extractor.ExtractPages(record.FilePath).ToList();

            foreach (var structuredPage in pages)
            {
                if (structuredPage.RawText.Length < ingestionOptions.Value.MinPageCharacters)
                    continue;

                var promptText = BuildPromptText(structuredPage.Blocks);

                if (!tocMap.IsEmpty)
                {
                    var category = tocMap.GetCategory(structuredPage.PageNumber);
                    if (category is null) continue;

                    LogClassifyingPage(logger, structuredPage.PageNumber, pages.Count, recordId);
                    var extracted = await entityExtractor.ExtractAsync(
                        promptText, category.Value.ToString(), structuredPage.PageNumber,
                        record.DisplayName, record.Version, cancellationToken);

                    if (extracted.Count > 0)
                        await jsonStore.SavePageAsync(recordId, structuredPage, extracted, cancellationToken);
                }
                else
                {
                    LogClassifyingPage(logger, structuredPage.PageNumber, pages.Count, recordId);
                    var types = await classifier.ClassifyPageAsync(structuredPage.RawText, cancellationToken);
                    var pageEntities = new List<ExtractedEntity>();

                    foreach (var type in types)
                    {
                        var extracted = await entityExtractor.ExtractAsync(
                            promptText, type, structuredPage.PageNumber,
                            record.DisplayName, record.Version, cancellationToken);
                        pageEntities.AddRange(extracted);
                    }

                    if (pageEntities.Count > 0)
                        await jsonStore.SavePageAsync(recordId, structuredPage, pageEntities, cancellationToken);
                }

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
```

- [ ] **Step 5.3: Add IngestJsonAsync chunk count fix**

In `IngestJsonAsync`, replace the chunk count query (it used to access `IReadOnlyList<IReadOnlyList<ExtractedEntity>>`):

```csharp
var pages = await jsonStore.LoadAllPagesAsync(recordId, cancellationToken);
var chunkCount = pages.Sum(p => p.Entities.Count(e =>
    !string.IsNullOrWhiteSpace(e.Data["description"]?.GetValue<string>())));
```

- [ ] **Step 5.4: Add BuildPromptText helper and ExtractSinglePageAsync**

Add these methods inside `IngestionOrchestrator` (before the private log methods):

```csharp
public async Task<PageData?> ExtractSinglePageAsync(
    int bookId, int pageNumber, bool save, CancellationToken ct = default)
{
    var record = await tracker.GetByIdAsync(bookId, ct);
    if (record is null) return null;

    var structuredPage = extractor.ExtractSinglePage(record.FilePath, pageNumber);
    if (structuredPage is null) return null;

    var promptText = BuildPromptText(structuredPage.Blocks);
    var bookmarks  = bookmarkReader.ReadBookmarks(record.FilePath);
    var tocMap     = await tocClassifier.ClassifyAsync(bookmarks, ct);

    List<ExtractedEntity> entities;
    var category = tocMap.IsEmpty ? null : tocMap.GetCategory(pageNumber);

    if (category is not null)
    {
        entities = [.. await entityExtractor.ExtractAsync(
            promptText, category.Value.ToString(), pageNumber,
            record.DisplayName, record.Version, ct)];
    }
    else
    {
        var types = await classifier.ClassifyPageAsync(structuredPage.RawText, ct);
        entities = [];
        foreach (var type in types)
        {
            var extracted = await entityExtractor.ExtractAsync(
                promptText, type, pageNumber, record.DisplayName, record.Version, ct);
            entities.AddRange(extracted);
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
```

- [ ] **Step 5.5: Update IngestionOrchestratorTests**

In `DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs`:

Replace the `IPdfTextExtractor _extractor` field with `IPdfStructuredExtractor _extractor = Substitute.For<IPdfStructuredExtractor>();`.

Update `BuildSut()` to pass `_extractor` (same position, new type).

Update every mock setup that called `_extractor.ExtractPages(...)` returning `(int, string)` tuples to instead return `StructuredPage` objects:

```csharp
_extractor.ExtractPages(Arg.Any<string>())
    .Returns([new StructuredPage(1, "page text", [new PageBlock(1, "body", "page text")])]);
```

Update the `IEntityJsonStore.SavePageAsync` mock expectations — method now takes `(int bookId, StructuredPage page, IReadOnlyList<ExtractedEntity> entities, CancellationToken)`.

Update `LoadAllPagesAsync` mocks to return `IReadOnlyList<PageData>`:

```csharp
_jsonStore.LoadAllPagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
    .Returns(new List<PageData>
    {
        new PageData(1, "text", [],
            [new ExtractedEntity(1, "PHB", "Edition2014", false, "Spell", "Fireball",
                new JsonObject { ["description"] = "desc" })])
    });
```

- [ ] **Step 5.6: Run all orchestrator tests**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "IngestionOrchestratorTests"
```

Expected: all pass.

- [ ] **Step 5.7: Build to verify no errors**

```bash
dotnet build DndMcpAICsharpFun.csproj
```

- [ ] **Step 5.8: Commit**

```bash
git add Features/Ingestion/IIngestionOrchestrator.cs Features/Ingestion/IngestionOrchestrator.cs DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs
git commit -m "feat: wire IPdfStructuredExtractor into orchestrator; add BuildPromptText; add ExtractSinglePageAsync"
```

---

## Task 6: Qdrant PageEnd payload + collection index

**Files:**
- Modify: `Infrastructure/Qdrant/QdrantPayloadFields.cs`
- Modify: `Infrastructure/Qdrant/QdrantCollectionInitializer.cs`
- Modify: `Features/VectorStore/QdrantVectorStoreService.cs`

- [ ] **Step 6.1: Add PageEnd constant**

In `Infrastructure/Qdrant/QdrantPayloadFields.cs`, add:

```csharp
public const string PageEnd = "page_end";
```

Full file after edit:

```csharp
namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public static class QdrantPayloadFields
{
    public const string Text       = "text";
    public const string SourceBook = "source_book";
    public const string Version    = "version";
    public const string Category   = "category";
    public const string EntityName = "entity_name";
    public const string Chapter    = "chapter";
    public const string PageNumber = "page_number";
    public const string ChunkIndex = "chunk_index";
    public const string PageEnd    = "page_end";
}
```

- [ ] **Step 6.2: Add page_end index to QdrantCollectionInitializer**

In `CreatePayloadIndexesAsync`, add `QdrantPayloadFields.PageEnd` to `intFields`:

```csharp
string[] intFields = [QdrantPayloadFields.PageNumber, QdrantPayloadFields.ChunkIndex, QdrantPayloadFields.PageEnd];
```

- [ ] **Step 6.3: Write page_end into Qdrant point payload**

In `Features/VectorStore/QdrantVectorStoreService.cs`, in `BuildPoint`, add after the `EntityName` conditional:

```csharp
if (meta.PageEnd.HasValue)
    point.Payload[QdrantPayloadFields.PageEnd] = (long)meta.PageEnd.Value;
```

- [ ] **Step 6.4: Build and verify**

```bash
dotnet build DndMcpAICsharpFun.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 6.5: Commit**

```bash
git add Infrastructure/Qdrant/QdrantPayloadFields.cs Infrastructure/Qdrant/QdrantCollectionInitializer.cs Features/VectorStore/QdrantVectorStoreService.cs
git commit -m "feat: add page_end Qdrant payload field and collection index"
```

---

## Task 7: Single-page extract endpoint

**Files:**
- Modify: `Features/Admin/BooksAdminEndpoints.cs`
- Modify: `DndMcpAICsharpFun.http`

- [ ] **Step 7.1: Register new route in MapBooksAdmin**

In `Features/Admin/BooksAdminEndpoints.cs`, in `MapBooksAdmin`, add:

```csharp
group.MapPost("/books/{id:int}/extract-page/{pageNumber:int}", ExtractPage).DisableAntiforgery();
```

- [ ] **Step 7.2: Add ExtractPage handler**

Add this method to `BooksAdminEndpoints`:

```csharp
private static async Task<IResult> ExtractPage(
    int id,
    int pageNumber,
    [FromQuery] bool save,
    IIngestionOrchestrator orchestrator,
    IPdfStructuredExtractor extractor,
    IIngestionTracker tracker,
    CancellationToken ct)
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

    var pageData = await orchestrator.ExtractSinglePageAsync(id, pageNumber, save, ct);
    if (pageData is null)
        return Results.NotFound($"Book with id {id} not found");

    return Results.Ok(pageData);
}
```

Add the required using at the top of `BooksAdminEndpoints.cs`:

```csharp
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
```

- [ ] **Step 7.3: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 7.4: Update DndMcpAICsharpFun.http**

Add to `DndMcpAICsharpFun.http`:

```http
### Extract single page (no save — inspect result only)
POST {{baseUrl}}/admin/books/1/extract-page/51
X-Api-Key: {{adminApiKey}}

### Extract single page and persist to disk
POST {{baseUrl}}/admin/books/1/extract-page/51?save=true
X-Api-Key: {{adminApiKey}}
```

- [ ] **Step 7.5: Commit**

```bash
git add Features/Admin/BooksAdminEndpoints.cs DndMcpAICsharpFun.http
git commit -m "feat: add POST /admin/books/{id}/extract-page/{pageNumber} synchronous test endpoint"
```

---

## Task 8: DI wiring — swap extractor, remove old extractor

**Files:**
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Delete: `Features/Ingestion/Pdf/IPdfTextExtractor.cs`
- Delete: `Features/Ingestion/Pdf/PdfPigTextExtractor.cs`
- Modify: `DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigTextExtractorTests.cs` (delete or repurpose)

- [ ] **Step 8.1: Update DI registration**

In `Extensions/ServiceCollectionExtensions.cs`, in `AddIngestionPipeline`, replace:

```csharp
services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
```

With:

```csharp
services.AddSingleton<IPdfStructuredExtractor, PdfPigStructuredExtractor>();
```

Update the using at the top if needed (both types are in `Features.Ingestion.Pdf`).

- [ ] **Step 8.2: Delete old extractor files**

```bash
rm Features/Ingestion/Pdf/IPdfTextExtractor.cs
rm Features/Ingestion/Pdf/PdfPigTextExtractor.cs
rm DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigTextExtractorTests.cs
```

- [ ] **Step 8.3: Build to confirm everything compiles**

```bash
dotnet build DndMcpAICsharpFun.csproj
dotnet build DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj
```

Expected: both `Build succeeded.`

- [ ] **Step 8.4: Run full test suite**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj
```

Expected: all pass.

- [ ] **Step 8.5: Commit**

```bash
git add Extensions/ServiceCollectionExtensions.cs
git rm Features/Ingestion/Pdf/IPdfTextExtractor.cs Features/Ingestion/Pdf/PdfPigTextExtractor.cs DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigTextExtractorTests.cs
git commit -m "feat: swap IPdfTextExtractor for IPdfStructuredExtractor in DI; delete old extractor"
```

---

## Task 9: Embedding model config upgrade

**Files:**
- Modify: `Config/appsettings.json`
- Modify: `docker-compose.yml`

- [ ] **Step 9.1: Update appsettings.json**

In `Config/appsettings.json`, change:

```json
"Ollama": {
  "BaseUrl": "http://ollama:11434",
  "EmbeddingModel": "mxbai-embed-large",
  "ExtractionModel": "llama3.2"
},
"Qdrant": {
  "Host": "qdrant",
  "Port": 6334,
  "ApiKey": null,
  "VectorSize": 1024,
  "CollectionName": "dnd_chunks"
},
```

- [ ] **Step 9.2: Update docker-compose.yml**

Find the `ollama-pull` init container command (it uses `ollama pull`). Add `mxbai-embed-large` to the pull list. The exact service depends on your compose structure — find the init container that runs `ollama pull nomic-embed-text` and add:

```yaml
command: >
  sh -c "ollama pull nomic-embed-text &&
         ollama pull mxbai-embed-large &&
         ollama pull llama3.2"
```

Also add a comment above the `qdrant_data` volume:

```yaml
volumes:
  # WARNING: Changing Qdrant:VectorSize in appsettings.json requires wiping this volume.
  # Run: docker compose down -v && docker compose up -d
  qdrant_data:
```

- [ ] **Step 9.3: Build to verify config is valid**

```bash
dotnet build DndMcpAICsharpFun.csproj
```

- [ ] **Step 9.4: Commit**

```bash
git add Config/appsettings.json docker-compose.yml
git commit -m "feat: upgrade embedding model to mxbai-embed-large (1024 dims); note Qdrant volume wipe requirement"
```

---

## Task 10: Final test run and admin endpoint integration test

**Files:**
- Modify: `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs`

- [ ] **Step 10.1: Add extract-page endpoint test**

Open `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs`. Add a test for the new endpoint:

```csharp
[Fact]
public async Task ExtractPage_UnknownBook_Returns404()
{
    // Arrange — use the existing test WebApplicationFactory pattern from this file
    // IIngestionTracker returns null for unknown id
    _tracker.GetByIdAsync(999, Arg.Any<CancellationToken>())
        .Returns((IngestionRecord?)null);

    using var client = BuildClient(); // use same helper as other tests in this file

    // Act
    var response = await client.PostAsync("/admin/books/999/extract-page/1", null);

    // Assert
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}
```

(Follow the exact `BuildClient()` / factory pattern already used in the file — do not introduce a new pattern.)

- [ ] **Step 10.2: Run full test suite**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj
```

Expected: all pass.

- [ ] **Step 10.3: Final commit**

```bash
git add DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs
git commit -m "test: add extract-page 404 test; all tests passing"
```
