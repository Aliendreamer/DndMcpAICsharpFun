# TOC-Guided Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce LLM extraction calls from 8 per page to 1 by reading the PDF's embedded bookmark outline and using the LLM once per book to map chapter ranges to `ContentCategory` values; also add a `POST /admin/books/{id}/cancel-extract` endpoint that stops extraction in progress, deletes partial JSON output, and resets the book to `Pending`.

**Architecture:** At extraction start, `PdfPigBookmarkReader` reads the bookmark tree from the PDF file; `OllamaTocCategoryClassifier` sends the bookmark titles to Ollama once and parses back a `TocCategoryMap` (page-range → `ContentCategory?` lookup). `IngestionOrchestrator.ExtractBookAsync` consults the map per page and calls only the one relevant extractor — or skips the page entirely if no category applies. Per-job `CancellationTokenSource` instances are tracked in a singleton `ExtractionCancellationRegistry`; the cancel endpoint triggers cleanup and status reset.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, PdfPig (already present), OllamaSharp (already present), xUnit + NSubstitute (test project)

---

## File Map

**New files:**
- `Features/Ingestion/Pdf/PdfBookmark.cs` — `record PdfBookmark(string Title, int PageNumber)`
- `Features/Ingestion/Pdf/IPdfBookmarkReader.cs` — interface
- `Features/Ingestion/Pdf/PdfPigBookmarkReader.cs` — PdfPig implementation
- `Features/Ingestion/Extraction/TocCategoryMap.cs` — page-range lookup value type
- `Features/Ingestion/Extraction/ITocCategoryClassifier.cs` — interface
- `Features/Ingestion/Extraction/OllamaTocCategoryClassifier.cs` — Ollama LLM implementation
- `Features/Ingestion/IExtractionCancellationRegistry.cs` — interface
- `Features/Ingestion/ExtractionCancellationRegistry.cs` — singleton implementation
- `DndMcpAICsharpFun.Tests/Ingestion/Extraction/TocCategoryMapTests.cs`
- `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocCategoryClassifierTests.cs`
- `DndMcpAICsharpFun.Tests/Ingestion/ExtractionCancellationRegistryTests.cs`

**Modified files:**
- `Features/Ingestion/IngestionOrchestrator.cs` — inject new services, update `ExtractBookAsync`
- `Features/Ingestion/IngestionQueueWorker.cs` — register/unregister CTS around Extract work items
- `Features/Admin/BooksAdminEndpoints.cs` — add `CancelExtract` endpoint + register in `MapBooksAdmin`
- `Extensions/ServiceCollectionExtensions.cs` — register new services
- `DndMcpAICsharpFun.http` — add cancel-extract example request

---

## Task 1: PdfBookmark record + IPdfBookmarkReader + PdfPigBookmarkReader

**Files:**
- Create: `Features/Ingestion/Pdf/PdfBookmark.cs`
- Create: `Features/Ingestion/Pdf/IPdfBookmarkReader.cs`
- Create: `Features/Ingestion/Pdf/PdfPigBookmarkReader.cs`

- [ ] **Step 1.1: Create PdfBookmark record and interface**

`Features/Ingestion/Pdf/PdfBookmark.cs`:
```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed record PdfBookmark(string Title, int PageNumber);
```

`Features/Ingestion/Pdf/IPdfBookmarkReader.cs`:
```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public interface IPdfBookmarkReader
{
    IReadOnlyList<PdfBookmark> ReadBookmarks(string filePath);
}
```

- [ ] **Step 1.2: Implement PdfPigBookmarkReader**

`Features/Ingestion/Pdf/PdfPigBookmarkReader.cs`:
```csharp
using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed partial class PdfPigBookmarkReader(
    ILogger<PdfPigBookmarkReader> logger) : IPdfBookmarkReader
{
    public IReadOnlyList<PdfBookmark> ReadBookmarks(string filePath)
    {
        using var document = PdfDocument.Open(filePath);

        if (!document.TryGetBookmarks(out var bookmarks))
        {
            LogNoBookmarks(logger, Path.GetFileName(filePath));
            return [];
        }

        var result = new List<PdfBookmark>();
        Flatten(bookmarks.Roots, result);
        return result;
    }

    private static void Flatten(IReadOnlyList<BookmarkNode> nodes, List<PdfBookmark> result)
    {
        foreach (var node in nodes)
        {
            if (node is DocumentBookmarkNode docNode && docNode.PageNumber.HasValue)
                result.Add(new PdfBookmark(node.Title, docNode.PageNumber.Value));

            if (node.Children.Count > 0)
                Flatten(node.Children, result);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No embedded bookmarks found in {FileName} — falling back to all-categories extraction")]
    private static partial void LogNoBookmarks(ILogger logger, string fileName);
}
```

- [ ] **Step 1.3: Build and verify compilation**

```bash
dotnet build DndMcpAICsharpFun.csproj
```
Expected: 0 errors

- [ ] **Step 1.4: Commit**

```bash
git add Features/Ingestion/Pdf/PdfBookmark.cs Features/Ingestion/Pdf/IPdfBookmarkReader.cs Features/Ingestion/Pdf/PdfPigBookmarkReader.cs
git commit -m "feat: add IPdfBookmarkReader and PdfPigBookmarkReader"
```

---

## Task 2: TocCategoryMap + ITocCategoryClassifier + OllamaTocCategoryClassifier

**Files:**
- Create: `Features/Ingestion/Extraction/TocCategoryMap.cs`
- Create: `Features/Ingestion/Extraction/ITocCategoryClassifier.cs`
- Create: `Features/Ingestion/Extraction/OllamaTocCategoryClassifier.cs`
- Create: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/TocCategoryMapTests.cs`
- Create: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocCategoryClassifierTests.cs`

- [ ] **Step 2.1: Write failing TocCategoryMap tests**

`DndMcpAICsharpFun.Tests/Ingestion/Extraction/TocCategoryMapTests.cs`:
```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public class TocCategoryMapTests
{
    [Fact]
    public void GetCategory_ReturnsNull_WhenNoRanges()
    {
        var map = new TocCategoryMap([]);
        Assert.Null(map.GetCategory(5));
    }

    [Fact]
    public void GetCategory_ReturnsNull_WhenPageBeforeFirstBookmark()
    {
        var map = new TocCategoryMap([(10, ContentCategory.Spell)]);
        Assert.Null(map.GetCategory(5));
    }

    [Fact]
    public void GetCategory_ReturnsCategory_WhenPageWithinRange()
    {
        var map = new TocCategoryMap([
            (10, ContentCategory.Rule),
            (45, ContentCategory.Class),
            (200, ContentCategory.Spell)
        ]);
        Assert.Equal(ContentCategory.Class, map.GetCategory(100));
        Assert.Equal(ContentCategory.Spell, map.GetCategory(250));
        Assert.Equal(ContentCategory.Rule, map.GetCategory(10));
    }

    [Fact]
    public void GetCategory_ReturnsNull_ForNullMappedRange()
    {
        var map = new TocCategoryMap([
            (1, null),   // introduction
            (45, ContentCategory.Class),
        ]);
        Assert.Null(map.GetCategory(3));
        Assert.Equal(ContentCategory.Class, map.GetCategory(45));
    }

    [Fact]
    public void IsEmpty_True_WhenNoRanges()
    {
        Assert.True(new TocCategoryMap([]).IsEmpty);
    }
}
```

- [ ] **Step 2.2: Run to confirm failure**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "TocCategoryMapTests" -v n
```
Expected: FAIL with type-not-found errors

- [ ] **Step 2.3: Implement TocCategoryMap**

`Features/Ingestion/Extraction/TocCategoryMap.cs`:
```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed class TocCategoryMap
{
    private readonly (int StartPage, ContentCategory? Category)[] _ranges;

    public TocCategoryMap(IEnumerable<(int StartPage, ContentCategory? Category)> ranges)
    {
        _ranges = ranges.OrderBy(static r => r.StartPage).ToArray();
    }

    public bool IsEmpty => _ranges.Length == 0;

    public ContentCategory? GetCategory(int pageNumber)
    {
        ContentCategory? result = null;
        foreach (var (startPage, category) in _ranges)
        {
            if (startPage > pageNumber) break;
            result = category;
        }
        return result;
    }
}
```

- [ ] **Step 2.4: Run tests to confirm they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "TocCategoryMapTests" -v n
```
Expected: 5 tests PASS

- [ ] **Step 2.5: Create ITocCategoryClassifier interface**

`Features/Ingestion/Extraction/ITocCategoryClassifier.cs`:
```csharp
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public interface ITocCategoryClassifier
{
    Task<TocCategoryMap> ClassifyAsync(
        IReadOnlyList<PdfBookmark> bookmarks,
        CancellationToken ct = default);
}
```

- [ ] **Step 2.6: Write failing OllamaTocCategoryClassifier tests**

`DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocCategoryClassifierTests.cs`:
```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public class OllamaTocCategoryClassifierTests
{
    private static OllamaOptions DefaultOptions() => new() { ExtractionModel = "llama3.2" };

    [Fact]
    public async Task ClassifyAsync_ReturnsEmptyMap_WhenBookmarksEmpty()
    {
        var ollama = Substitute.For<OllamaApiClient>();
        var sut = new OllamaTocCategoryClassifier(
            ollama,
            Options.Create(DefaultOptions()),
            NullLogger<OllamaTocCategoryClassifier>.Instance);

        var map = await sut.ClassifyAsync([], CancellationToken.None);

        Assert.True(map.IsEmpty);
    }

    [Fact]
    public async Task ClassifyAsync_ReturnsEmptyMap_WhenLlmReturnsInvalidJson()
    {
        var ollama = Substitute.For<OllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsyncChunks("not json at all"));

        var sut = new OllamaTocCategoryClassifier(
            ollama,
            Options.Create(DefaultOptions()),
            NullLogger<OllamaTocCategoryClassifier>.Instance);

        var bookmarks = new[] { new PdfBookmark("Chapter 11: Spells", 200) };
        var map = await sut.ClassifyAsync(bookmarks, CancellationToken.None);

        Assert.True(map.IsEmpty);
    }

    [Fact]
    public async Task ClassifyAsync_ParsesValidResponse_IntoMap()
    {
        var json = """
            [
              {"startPage": 1,   "category": null},
              {"startPage": 45,  "category": "Class"},
              {"startPage": 200, "category": "Spell"}
            ]
            """;

        var ollama = Substitute.For<OllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsyncChunks(json));

        var sut = new OllamaTocCategoryClassifier(
            ollama,
            Options.Create(DefaultOptions()),
            NullLogger<OllamaTocCategoryClassifier>.Instance);

        var bookmarks = new[]
        {
            new PdfBookmark("Introduction", 1),
            new PdfBookmark("Chapter 3: Classes", 45),
            new PdfBookmark("Chapter 11: Spells", 200),
        };

        var map = await sut.ClassifyAsync(bookmarks, CancellationToken.None);

        Assert.Null(map.GetCategory(3));
        Assert.Equal(ContentCategory.Class, map.GetCategory(80));
        Assert.Equal(ContentCategory.Spell, map.GetCategory(250));
    }

    private static async IAsyncEnumerable<ChatResponseStream?> AsyncChunks(string text)
    {
        yield return new ChatResponseStream { Message = new Message { Content = text } };
        await Task.CompletedTask;
    }
}
```

- [ ] **Step 2.7: Run to confirm failure**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "OllamaTocCategoryClassifierTests" -v n
```
Expected: FAIL with type-not-found errors

- [ ] **Step 2.8: Implement OllamaTocCategoryClassifier**

`Features/Ingestion/Extraction/OllamaTocCategoryClassifier.cs`:
```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed partial class OllamaTocCategoryClassifier(
    OllamaApiClient ollama,
    IOptions<OllamaOptions> options,
    ILogger<OllamaTocCategoryClassifier> logger) : ITocCategoryClassifier
{
    private const string SystemPrompt =
        """
        You are a D&D rulebook chapter classifier.
        Given a list of PDF chapter titles and their starting page numbers, determine which
        D&D content category best matches each chapter.

        Valid categories: Spell, Monster, Class, Background, Item, Rule, Treasure, Encounter, Trap
        Use null if no category applies (e.g. Introduction, Preface, Index, Table of Contents).

        Return a JSON array, one entry per bookmark in the same order:
        [{"startPage": N, "category": "Spell"|null}, ...]

        Reply with JSON only, no explanation.
        """;

    private readonly string _model = options.Value.ExtractionModel;

    public async Task<TocCategoryMap> ClassifyAsync(
        IReadOnlyList<PdfBookmark> bookmarks,
        CancellationToken ct = default)
    {
        if (bookmarks.Count == 0)
            return new TocCategoryMap([]);

        var input = JsonSerializer.Serialize(
            bookmarks.Select(static b => new { title = b.Title, startPage = b.PageNumber }));

        var request = new ChatRequest
        {
            Model = _model,
            Stream = true,
            Format = "json",
            Messages =
            [
                new Message { Role = ChatRole.System, Content = SystemPrompt },
                new Message { Role = ChatRole.User, Content = input }
            ]
        };

        var sb = new StringBuilder();
        await foreach (var chunk in ollama.ChatAsync(request, ct))
            sb.Append(chunk?.Message?.Content ?? string.Empty);

        var json = sb.ToString().Trim();

        try
        {
            var array = JsonNode.Parse(json)?.AsArray();
            if (array is null) return FallbackMap(json);

            var ranges = new List<(int, ContentCategory?)>();
            foreach (var node in array)
            {
                if (node is not JsonObject obj) continue;
                var startPage = obj["startPage"]?.GetValue<int>() ?? 0;
                var categoryStr = obj["category"]?.GetValue<string>();
                ContentCategory? category = Enum.TryParse<ContentCategory>(categoryStr, out var c) ? c : null;
                ranges.Add((startPage, category));
            }

            LogClassified(logger, ranges.Count, _model);
            return new TocCategoryMap(ranges);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return FallbackMap(json);
        }
    }

    private TocCategoryMap FallbackMap(string json)
    {
        LogInvalidJson(logger, json[..Math.Min(200, json.Length)]);
        return new TocCategoryMap([]);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "TOC classified {Count} ranges with {Model}")]
    private static partial void LogClassified(ILogger logger, int count, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TOC classifier returned invalid JSON: {Json} — falling back to all-categories")]
    private static partial void LogInvalidJson(ILogger logger, string json);
}
```

- [ ] **Step 2.9: Run tests to confirm they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "OllamaTocCategoryClassifierTests" -v n
```
Expected: 3 tests PASS

- [ ] **Step 2.10: Commit**

```bash
git add Features/Ingestion/Extraction/TocCategoryMap.cs Features/Ingestion/Extraction/ITocCategoryClassifier.cs Features/Ingestion/Extraction/OllamaTocCategoryClassifier.cs DndMcpAICsharpFun.Tests/Ingestion/Extraction/TocCategoryMapTests.cs DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocCategoryClassifierTests.cs
git commit -m "feat: add TocCategoryMap, ITocCategoryClassifier, and OllamaTocCategoryClassifier"
```

---

## Task 3: IExtractionCancellationRegistry + ExtractionCancellationRegistry

**Files:**
- Create: `Features/Ingestion/IExtractionCancellationRegistry.cs`
- Create: `Features/Ingestion/ExtractionCancellationRegistry.cs`
- Create: `DndMcpAICsharpFun.Tests/Ingestion/ExtractionCancellationRegistryTests.cs`

- [ ] **Step 3.1: Write failing tests**

`DndMcpAICsharpFun.Tests/Ingestion/ExtractionCancellationRegistryTests.cs`:
```csharp
using DndMcpAICsharpFun.Features.Ingestion;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public class ExtractionCancellationRegistryTests
{
    [Fact]
    public void Cancel_ReturnsFalse_WhenNothingRegistered()
    {
        var registry = new ExtractionCancellationRegistry();
        Assert.False(registry.Cancel(42));
    }

    [Fact]
    public void Cancel_ReturnsTrue_AndCancelsToken_WhenRegistered()
    {
        var registry = new ExtractionCancellationRegistry();
        using var cts = new CancellationTokenSource();
        registry.Register(1, cts);

        var result = registry.Cancel(1);

        Assert.True(result);
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_ReturnsFalse_AfterUnregister()
    {
        var registry = new ExtractionCancellationRegistry();
        using var cts = new CancellationTokenSource();
        registry.Register(1, cts);
        registry.Unregister(1);

        Assert.False(registry.Cancel(1));
    }

    [Fact]
    public void Register_OverwritesPrevious_WhenSameBookId()
    {
        var registry = new ExtractionCancellationRegistry();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        registry.Register(1, cts1);
        registry.Register(1, cts2);
        registry.Cancel(1);

        Assert.True(cts2.IsCancellationRequested);
        Assert.False(cts1.IsCancellationRequested);
    }
}
```

- [ ] **Step 3.2: Run to confirm failure**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "ExtractionCancellationRegistryTests" -v n
```
Expected: FAIL

- [ ] **Step 3.3: Create interface and implementation**

`Features/Ingestion/IExtractionCancellationRegistry.cs`:
```csharp
namespace DndMcpAICsharpFun.Features.Ingestion;

public interface IExtractionCancellationRegistry
{
    void Register(int bookId, CancellationTokenSource cts);
    bool Cancel(int bookId);
    void Unregister(int bookId);
}
```

`Features/Ingestion/ExtractionCancellationRegistry.cs`:
```csharp
using System.Collections.Concurrent;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed class ExtractionCancellationRegistry : IExtractionCancellationRegistry
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _active = new();

    public void Register(int bookId, CancellationTokenSource cts) =>
        _active[bookId] = cts;

    public bool Cancel(int bookId)
    {
        if (!_active.TryGetValue(bookId, out var cts)) return false;
        cts.Cancel();
        return true;
    }

    public void Unregister(int bookId) =>
        _active.TryRemove(bookId, out _);
}
```

- [ ] **Step 3.4: Run tests to confirm they pass**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "ExtractionCancellationRegistryTests" -v n
```
Expected: 4 tests PASS

- [ ] **Step 3.5: Commit**

```bash
git add Features/Ingestion/IExtractionCancellationRegistry.cs Features/Ingestion/ExtractionCancellationRegistry.cs DndMcpAICsharpFun.Tests/Ingestion/ExtractionCancellationRegistryTests.cs
git commit -m "feat: add IExtractionCancellationRegistry and ExtractionCancellationRegistry"
```

---

## Task 4: Update IngestionOrchestrator.ExtractBookAsync

**Files:**
- Modify: `Features/Ingestion/IngestionOrchestrator.cs`

The current `IngestionOrchestrator` constructor injects `ILlmClassifier` and `ILlmEntityExtractor`. We add `IPdfBookmarkReader` and `ITocCategoryClassifier`. The `ExtractBookAsync` method gets a TOC pre-pass and per-page category dispatch. The `OperationCanceledException` handler resets status to `Pending`.

- [ ] **Step 4.1: Verify current IngestionOrchestrator constructor (no change needed)**

The constructor currently looks like this — confirm by reading the file before editing:
```csharp
public sealed partial class IngestionOrchestrator(
    IIngestionTracker tracker,
    IPdfTextExtractor extractor,
    ILlmClassifier classifier,
    ILlmEntityExtractor entityExtractor,
    IEntityJsonStore jsonStore,
    IOptions<IngestionOptions> ingestionOptions,
    ILogger<IngestionOrchestrator> logger) : IIngestionOrchestrator
```
Add `IPdfBookmarkReader bookmarkReader` and `ITocCategoryClassifier tocClassifier` after `extractor` in the parameter list.

- [ ] **Step 4.2: Update the constructor and ExtractBookAsync**

Find the class declaration line. The constructor currently takes:
```csharp
public sealed partial class IngestionOrchestrator(
    IIngestionTracker tracker,
    IPdfTextExtractor extractor,
    ILlmClassifier classifier,
    ILlmEntityExtractor entityExtractor,
    IEntityJsonStore jsonStore,
    IOptions<IngestionOptions> ingestionOptions,
    ILogger<IngestionOrchestrator> logger) : IIngestionOrchestrator
```

Add `IPdfBookmarkReader bookmarkReader` and `ITocCategoryClassifier tocClassifier` to the constructor parameters. Then replace `ExtractBookAsync` body with:

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
        var currentHash = string.IsNullOrEmpty(record.FileHash)
            ? await ComputeHashAsync(record.FilePath, cancellationToken)
            : record.FileHash;
        await tracker.MarkHashAsync(recordId, currentHash, cancellationToken);

        // Build TOC category map — one LLM call per book
        var bookmarks = bookmarkReader.ReadBookmarks(record.FilePath);
        var tocMap = bookmarks.Count > 0
            ? await tocClassifier.ClassifyAsync(bookmarks, cancellationToken)
            : new TocCategoryMap([]);

        var pages = extractor.ExtractPages(record.FilePath).ToList();

        foreach (var (pageNumber, pageText) in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pageText.Length < ingestionOptions.Value.MinPageCharacters)
                continue;

            if (!tocMap.IsEmpty)
            {
                // TOC-guided: run at most one extractor pass
                var category = tocMap.GetCategory(pageNumber);
                if (category is null)
                    continue; // intro/index page — skip entirely

                LogClassifyingPage(logger, pageNumber, pages.Count, recordId);
                var extracted = await entityExtractor.ExtractAsync(
                    pageText, category.Value.ToString(), pageNumber,
                    record.DisplayName, record.Version, cancellationToken);

                if (extracted.Count > 0)
                    await jsonStore.SavePageAsync(recordId, pageNumber, extracted, cancellationToken);
            }
            else
            {
                // No bookmarks — fall back to per-page classifier (original behaviour)
                LogClassifyingPage(logger, pageNumber, pages.Count, recordId);
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

            LogExtractedPage(logger, pageNumber, pages.Count, recordId);
        }

        await tracker.MarkExtractedAsync(recordId, cancellationToken);
        LogExtractedBook(logger, record.DisplayName, recordId);
    }
    catch (OperationCanceledException)
    {
        LogExtractionCancelled(logger, record.DisplayName, recordId);
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

Also add the new log message at the bottom of the class:
```csharp
[LoggerMessage(Level = LogLevel.Information, Message = "Extraction cancelled for {BookName} (id={BookId}) — partial files deleted, status reset to Pending")]
private static partial void LogExtractionCancelled(ILogger logger, string bookName, int bookId);
```

- [ ] **Step 4.3: Build to verify**

```bash
dotnet build DndMcpAICsharpFun.csproj
```
Expected: 0 errors

- [ ] **Step 4.4: Commit**

```bash
git add Features/Ingestion/IngestionOrchestrator.cs
git commit -m "feat: TOC-guided extraction dispatch in IngestionOrchestrator"
```

---

## Task 5: Update IngestionQueueWorker for per-job CTS

**Files:**
- Modify: `Features/Ingestion/IngestionQueueWorker.cs`

- [ ] **Step 5.1: Inject IExtractionCancellationRegistry and wrap Extract work items**

The current `ExecuteAsync` dispatches all three work types with `stoppingToken`. Update to create a linked CTS for `Extract` items and register/unregister it:

Add `IExtractionCancellationRegistry cancellationRegistry` to the constructor. Update `ExecuteAsync` so the `Extract` branch becomes:

```csharp
IngestionWorkType.Extract => async () =>
{
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
    cancellationRegistry.Register(item.BookId, linkedCts);
    try
    {
        await orchestrator.ExtractBookAsync(item.BookId, linkedCts.Token);
    }
    finally
    {
        cancellationRegistry.Unregister(item.BookId);
    }
}
```

The full updated `ExecuteAsync`:
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IIngestionOrchestrator>();

            LogWorkItemStarted(logger, item.Type, item.BookId);
            var sw = Stopwatch.StartNew();

            if (item.Type == IngestionWorkType.Extract)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cancellationRegistry.Register(item.BookId, linkedCts);
                try
                {
                    await orchestrator.ExtractBookAsync(item.BookId, linkedCts.Token);
                }
                finally
                {
                    cancellationRegistry.Unregister(item.BookId);
                }
            }
            else
            {
                await (item.Type switch
                {
                    IngestionWorkType.Reingest   => orchestrator.IngestBookAsync(item.BookId, stoppingToken),
                    IngestionWorkType.IngestJson => orchestrator.IngestJsonAsync(item.BookId, stoppingToken),
                    _ => Task.CompletedTask
                });
            }

            LogWorkItemCompleted(logger, item.Type, item.BookId, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogUnhandledError(logger, ex, item.Type, item.BookId);
        }
    }
}
```

- [ ] **Step 5.2: Build to verify**

```bash
dotnet build DndMcpAICsharpFun.csproj
```
Expected: 0 errors

- [ ] **Step 5.3: Commit**

```bash
git add Features/Ingestion/IngestionQueueWorker.cs
git commit -m "feat: register per-job CTS in IngestionQueueWorker for extraction cancellation"
```

---

## Task 6: Add cancel-extract admin endpoint

**Files:**
- Modify: `Features/Admin/BooksAdminEndpoints.cs`
- Modify: `DndMcpAICsharpFun.http`

- [ ] **Step 6.1: Add CancelExtract method and register in MapBooksAdmin**

In `MapBooksAdmin`, add:
```csharp
group.MapPost("/books/{id:int}/cancel-extract", CancelExtract).DisableAntiforgery();
```

Add the handler method to `BooksAdminEndpoints`:
```csharp
private static IResult CancelExtract(int id, IExtractionCancellationRegistry cancellationRegistry)
{
    return cancellationRegistry.Cancel(id)
        ? Results.Ok($"Extraction for book {id} cancelled.")
        : Results.NotFound($"No active extraction found for book {id}.");
}
```

- [ ] **Step 6.2: Update DndMcpAICsharpFun.http**

Add after the `### Admin Books — Ingest from extracted JSON` block:
```
### Admin Books — Cancel in-progress extraction
POST {{baseUrl}}/admin/books/1/cancel-extract
X-Admin-Api-Key: {{adminKey}}
```

- [ ] **Step 6.3: Build to verify**

```bash
dotnet build DndMcpAICsharpFun.csproj
```
Expected: 0 errors

- [ ] **Step 6.4: Commit**

```bash
git add Features/Admin/BooksAdminEndpoints.cs DndMcpAICsharpFun.http
git commit -m "feat: add POST /admin/books/{id}/cancel-extract endpoint"
```

---

## Task 7: Register new services in ServiceCollectionExtensions

**Files:**
- Modify: `Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 7.1: Add registrations to AddIngestionPipeline**

In `AddIngestionPipeline`, add before `AddHostedService<QdrantCollectionInitializer>()`:
```csharp
services.AddSingleton<IPdfBookmarkReader, PdfPigBookmarkReader>();
services.AddScoped<ITocCategoryClassifier, OllamaTocCategoryClassifier>();
services.AddSingleton<IExtractionCancellationRegistry, ExtractionCancellationRegistry>();
```

`IExtractionCancellationRegistry` must be registered as a singleton so both `IngestionQueueWorker` (singleton) and the endpoint handler share the same instance. `ITocCategoryClassifier` is scoped to match `IngestionOrchestrator` (scoped).

- [ ] **Step 7.2: Build and run all tests**

```bash
dotnet build && dotnet test DndMcpAICsharpFun.Tests -v n
```
Expected: 0 build errors, all tests PASS

- [ ] **Step 7.3: Commit**

```bash
git add Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: register IPdfBookmarkReader, ITocCategoryClassifier, IExtractionCancellationRegistry"
```

---

## Task 8: Full integration smoke test

- [ ] **Step 8.1: Start the stack**

```bash
docker compose up -d
```
Wait for health checks to pass:
```bash
curl http://localhost:5101/health
```
Expected: `{"status":"Healthy",...}`

- [ ] **Step 8.2: Trigger extraction and verify TOC-guided behaviour**

```bash
# List registered books and note an id
curl -s -H "X-Admin-Api-Key: xxxxx" http://localhost:5101/admin/books | jq .

# Trigger extraction
curl -s -X POST -H "X-Admin-Api-Key: xxxxx" http://localhost:5101/admin/books/1/extract

# Watch logs — should see one "TOC classified N ranges" line, then per-page messages
docker logs dndmcpaicsharpfun-app-1 -f --tail=50
```
Expected: log line `TOC classified N ranges with llama3.2` appears, page logs show single-category extraction

- [ ] **Step 8.3: Test cancellation**

```bash
# Start extraction
curl -s -X POST -H "X-Admin-Api-Key: xxxxx" http://localhost:5101/admin/books/1/extract

# Cancel it immediately
curl -s -X POST -H "X-Admin-Api-Key: xxxxx" http://localhost:5101/admin/books/1/cancel-extract
```
Expected: `"Extraction for book 1 cancelled."` response. Check `./books/extracted/1/` — should be absent or empty. Book status in sqlite-web at http://localhost:8080 should show `Pending`.

- [ ] **Step 8.4: Test cancel on idle book returns 404**

```bash
curl -s -X POST -H "X-Admin-Api-Key: xxxxx" http://localhost:5101/admin/books/1/cancel-extract
```
Expected: 404 `"No active extraction found for book 1."`
