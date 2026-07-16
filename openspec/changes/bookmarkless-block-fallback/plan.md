# Bookmark-less Block-Ingestion Fallback — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Block ingestion must fall back to a full-coverage heading-derived TOC when a PDF has no embedded bookmarks, so every prose block is retained instead of the run aborting.

**Architecture:** The block extractor already converts the PDF (disk-cached) and has MinerU `section_header` items but discards them. Surface them via a new `PdfExtraction` return record. A new pure `FullCoverageHeadingTocMapper` turns those headings into a gap-free `TocCategoryMap` (per-heading titles, carry-forward category, catch-all fill). `BlockIngestionOrchestrator` uses that map in the empty-bookmarks branch instead of aborting. The bookmarked path is untouched.

**Tech Stack:** C# / .NET 10, xUnit + NSubstitute, MinerU via `IPdfStructureConverter`.

## Global Constraints

- Target framework `net10.0`; nullable enabled; **warnings-as-errors** (every project) — a build warning fails CI.
- Follow existing patterns: `sealed` types, primary constructors, `[LoggerMessage]` source-gen logging in the orchestrator.
- The bookmarked ingestion path MUST remain behaviorally identical (zero regression).
- Do NOT modify `HeadingTocMapper` (extraction path stays sparse/confident-only) — the new mapper is independent.
- Build/test commands in this repo run under the agent sandbox disabled (git-crypt) — the executor handles that; commands below are the plain `dotnet` invocations.

---

### Task 1: Surface section headers from the block extractor

**Files:**
- Create: `Features/Ingestion/Pdf/PdfExtraction.cs`
- Modify: `Features/Ingestion/Pdf/IPdfBlockExtractor.cs`
- Modify: `Features/Ingestion/Pdf/StructureBlockExtractor.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/Pdf/StructureBlockExtractorTests.cs`

**Interfaces:**
- Produces: `record PdfExtraction(IReadOnlyList<PdfBlock> Blocks, IReadOnlyList<PdfStructureItem> Headings)`; `IPdfBlockExtractor.ExtractBlocksAsync(string, CancellationToken) -> Task<PdfExtraction>`. `Headings` = the `doc.Items` whose `Type == "section_header"`, in document order.

- [ ] **Step 1: Update the two existing extractor tests to the new return shape (write failing test)**

In `StructureBlockExtractorTests.cs`, change both tests to read `.Blocks`, and add a headings assertion to the first. Replace the two `[Fact]` methods' result lines:

```csharp
// in ExtractBlocksAsync_MapsItemsToBlocks_PreservingPageAndOrder
var result = await sut.ExtractBlocksAsync("/tmp/fake.pdf");
var blocks = result.Blocks.ToList();

Assert.Equal(4, blocks.Count);
Assert.Equal("Wizard", blocks[0].Text);
Assert.Equal(112, blocks[0].PageNumber);
Assert.Equal(0, blocks[0].Order);
Assert.Equal(112, blocks[1].PageNumber);
Assert.Equal(1, blocks[1].Order);
Assert.Equal(113, blocks[2].PageNumber);
Assert.Equal(0, blocks[2].Order);
Assert.Equal(113, blocks[3].PageNumber);
Assert.Equal(1, blocks[3].Order);

// NEW: the section_header item is surfaced in Headings
Assert.Single(result.Headings);
Assert.Equal("Wizard", result.Headings[0].Text);
Assert.Equal(112, result.Headings[0].PageNumber);
```

```csharp
// in ExtractBlocksAsync_WhitespaceItem_Skipped
var result = await sut.ExtractBlocksAsync("/tmp/x.pdf");
var blocks = result.Blocks.ToList();

Assert.Equal(2, blocks.Count);
Assert.Equal("real", blocks[0].Text);
Assert.Equal("real 2", blocks[1].Text);
Assert.Empty(result.Headings);  // no section_header items → empty (not null)
```

- [ ] **Step 2: Run tests to verify they fail to compile**

Run: `dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "FullyQualifiedName~StructureBlockExtractorTests"`
Expected: build FAILS — `PdfExtraction` / `.Blocks` / `.Headings` don't exist yet.

- [ ] **Step 3: Create the `PdfExtraction` record**

`Features/Ingestion/Pdf/PdfExtraction.cs`:

```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// The result of extracting a PDF: the ordered prose blocks, plus the section-header
/// structure items from the same single conversion. Consumers use the headings to
/// build a fallback table of contents when the PDF has no embedded bookmarks, without
/// re-converting the file.
/// </summary>
public sealed record PdfExtraction(
    IReadOnlyList<PdfBlock> Blocks,
    IReadOnlyList<PdfStructureItem> Headings);
```

- [ ] **Step 4: Change the interface return type**

`Features/Ingestion/Pdf/IPdfBlockExtractor.cs`:

```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public interface IPdfBlockExtractor
{
    Task<PdfExtraction> ExtractBlocksAsync(string filePath, CancellationToken ct = default);
}
```

- [ ] **Step 5: Update `StructureBlockExtractor` to collect headings**

Replace the body of `ExtractBlocksAsync` in `Features/Ingestion/Pdf/StructureBlockExtractor.cs`:

```csharp
    public async Task<PdfExtraction> ExtractBlocksAsync(string filePath, CancellationToken ct = default)
    {
        var doc = await converter.ConvertAsync(filePath, ct);

        LogConverted(logger, Path.GetFileName(filePath), doc.Items.Count);

        var blocks = new List<PdfBlock>();
        var headings = new List<PdfStructureItem>();
        var perPageOrder = new Dictionary<int, int>();
        foreach (var item in doc.Items)
        {
            var text = item.Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            if (string.Equals(item.Type, "section_header", StringComparison.OrdinalIgnoreCase))
                headings.Add(item);

            var order = perPageOrder.TryGetValue(item.PageNumber, out var n) ? n : 0;
            perPageOrder[item.PageNumber] = order + 1;

            blocks.Add(new PdfBlock(text, item.PageNumber, order));
        }
        return new PdfExtraction(blocks, headings);
    }
```

- [ ] **Step 6: Run the extractor tests to verify they pass**

Run: `dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "FullyQualifiedName~StructureBlockExtractorTests"`
Expected: PASS (2 tests). (The orchestrator still won't compile — fixed in Task 3.)

- [ ] **Step 7: Commit**

```bash
git add Features/Ingestion/Pdf/PdfExtraction.cs Features/Ingestion/Pdf/IPdfBlockExtractor.cs Features/Ingestion/Pdf/StructureBlockExtractor.cs DndMcpAICsharpFun.Tests/Ingestion/Pdf/StructureBlockExtractorTests.cs
git commit -m "feat(ingestion): surface section headers from the block extractor (PdfExtraction)"
```

---

### Task 2: FullCoverageHeadingTocMapper

**Files:**
- Create: `Features/Ingestion/Pdf/FullCoverageHeadingTocMapper.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/Pdf/FullCoverageHeadingTocMapperTests.cs`

**Interfaces:**
- Consumes: `PdfStructureItem(string Type, string Text, int PageNumber, int? Level)`, `HeadingCategoryClassifier.Guess(string) -> ContentCategory`, `TocSectionEntry(string Title, ContentCategory? Category, int StartPage, int? EndPage = null)`, `TocCategoryMap`.
- Produces: `FullCoverageHeadingTocMapper.Map(IReadOnlyList<PdfStructureItem> headings, string bookTitle) -> IReadOnlyList<TocSectionEntry>`.

- [ ] **Step 1: Write the failing tests**

`DndMcpAICsharpFun.Tests/Ingestion/Pdf/FullCoverageHeadingTocMapperTests.cs`:

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class FullCoverageHeadingTocMapperTests
{
    private static PdfStructureItem H(string text, int page) => new("section_header", text, page, 1);

    [Fact]
    public void Map_EveryHeadingBecomesATitledEntry()
    {
        var entries = FullCoverageHeadingTocMapper.Map(
            new[] { H("Monsters", 10), H("Yuan-ti Anathema", 12) }, "Book");

        Assert.Equal("Monsters", entries[0].Title);
        Assert.Equal("Yuan-ti Anathema", entries[1].Title);
    }

    [Fact]
    public void Map_SubHeadingInheritsEnclosingConfidentCategory()
    {
        var entries = FullCoverageHeadingTocMapper.Map(
            new[] { H("Monsters", 10), H("Yuan-ti Anathema", 12) }, "Book");

        Assert.Equal(ContentCategory.Monster, entries[0].Category);
        Assert.Equal(ContentCategory.Monster, entries[1].Category); // carried forward
    }

    [Fact]
    public void Map_HeadingBeforeAnyConfidentCategoryDefaultsToRule()
    {
        var entries = FullCoverageHeadingTocMapper.Map(
            new[] { H("Yuan-ti Anathema", 12) }, "Book");

        Assert.Equal(ContentCategory.Rule, entries[^1].Category);
    }

    [Fact]
    public void Map_FirstHeadingAfterPageOne_PrependsFrontMatterCatchAll()
    {
        var entries = FullCoverageHeadingTocMapper.Map(new[] { H("Monsters", 10) }, "Book");

        Assert.Equal("Front Matter", entries[0].Title);
        Assert.Equal(1, entries[0].StartPage);
        Assert.Equal(ContentCategory.Rule, entries[0].Category);
    }

    [Fact]
    public void Map_NoHeadings_YieldsSingleWholeBookCatchAll()
    {
        var entries = FullCoverageHeadingTocMapper.Map(Array.Empty<PdfStructureItem>(), "My Book");

        Assert.Single(entries);
        Assert.Equal("My Book", entries[0].Title);
        Assert.Equal(1, entries[0].StartPage);
    }

    [Fact]
    public void Map_ProducesGapFreeCoverage_EveryPageResolves()
    {
        var entries = FullCoverageHeadingTocMapper.Map(
            new[] { H("Intro", 3), H("Monsters", 10) }, "Book");
        var map = new TocCategoryMap(entries);

        for (var page = 1; page <= 50; page++)
            Assert.NotNull(map.GetEntry(page)); // no page falls into a gap
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "FullyQualifiedName~FullCoverageHeadingTocMapperTests"`
Expected: build FAILS — `FullCoverageHeadingTocMapper` does not exist.

- [ ] **Step 3: Implement the mapper**

`Features/Ingestion/Pdf/FullCoverageHeadingTocMapper.cs`:

```csharp
using System.Collections.Frozen;

using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// Builds a FULL-COVERAGE table of contents from MinerU section-header items for the
/// block-ingestion fallback: every heading becomes a titled section boundary, categories
/// carry forward from the last confident heading, and catch-all entries guarantee that —
/// once assembled into a TocCategoryMap — every page from 1 onward resolves to a section
/// (no block is dropped).
///
/// Deliberately distinct from <see cref="HeadingTocMapper"/>, which is sparse/confident-only
/// for entity extraction (dropped headings there are correct; here they would lose content).
/// The confident-category set mirrors HeadingTocMapper.Confident — keep the two in sync.
/// </summary>
public static class FullCoverageHeadingTocMapper
{
    private static readonly FrozenSet<ContentCategory> Confident = new HashSet<ContentCategory>
    {
        ContentCategory.Spell, ContentCategory.Monster, ContentCategory.Class, ContentCategory.Race,
        ContentCategory.Background, ContentCategory.Item, ContentCategory.Condition, ContentCategory.God,
        ContentCategory.Plane, ContentCategory.Treasure, ContentCategory.Trap,
    }.ToFrozenSet();

    public static IReadOnlyList<TocSectionEntry> Map(
        IReadOnlyList<PdfStructureItem> headings, string bookTitle)
    {
        ArgumentNullException.ThrowIfNull(headings);

        var named = headings.Where(h => !string.IsNullOrWhiteSpace(h.Text)).ToList();
        if (named.Count == 0)
            return new[] { new TocSectionEntry(bookTitle, ContentCategory.Rule, 1) };

        var entries = new List<TocSectionEntry>();

        // Front-matter catch-all so pages before the first heading are never dropped.
        if (named[0].PageNumber > 1)
            entries.Add(new TocSectionEntry("Front Matter", ContentCategory.Rule, 1));

        ContentCategory? lastConfident = null;
        foreach (var h in named)
        {
            var guessed = HeadingCategoryClassifier.Guess(h.Text);
            ContentCategory category;
            if (Confident.Contains(guessed))
            {
                category = guessed;
                lastConfident = guessed;
            }
            else
            {
                category = lastConfident ?? ContentCategory.Rule;
            }

            entries.Add(new TocSectionEntry(h.Text.Trim(), category, h.PageNumber));
        }

        return entries;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "FullyQualifiedName~FullCoverageHeadingTocMapperTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/Pdf/FullCoverageHeadingTocMapper.cs DndMcpAICsharpFun.Tests/Ingestion/Pdf/FullCoverageHeadingTocMapperTests.cs
git commit -m "feat(ingestion): full-coverage heading TOC mapper for bookmark-less PDFs"
```

---

### Task 3: Wire the fallback into the orchestrator

**Files:**
- Modify: `Features/Ingestion/BlockIngestionOrchestrator.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/BlockIngestionOrchestratorTests.cs`

**Interfaces:**
- Consumes: `PdfExtraction` (Task 1), `FullCoverageHeadingTocMapper.Map` (Task 2). The orchestrator now calls `ExtractBlocksAsync` once, iterates `extraction.Blocks`, and builds `tocMap` from bookmarks OR `extraction.Headings`.

- [ ] **Step 1: Write the fallback test (and fix the existing mock return types)**

In `BlockIngestionOrchestratorTests.cs`, update the throwing test's mock return type (line ~42) from `Task.FromException<IReadOnlyList<PdfBlock>>` to `Task.FromException<PdfExtraction>`, and every other `ExtractBlocksAsync(...).Returns(...)` in the file to return a `PdfExtraction`. Then add this new test for the fallback path:

```csharp
[Fact]
public async Task IngestBlocksAsync_NoBookmarks_UsesHeadingFallbackAndIngests()
{
    const int bookId = 42;
    var record = new IngestionRecord
    {
        Id = bookId, FilePath = "/books/x.pdf", FileName = "x.pdf",
        FileHash = "hash42", DisplayName = "No-Bookmark Book", Version = "5e",
    };

    var tracker = Substitute.For<IIngestionTracker>();
    tracker.GetByIdAsync(bookId, Arg.Any<CancellationToken>()).Returns(record);

    // Long enough to clear MinBlockChars (40); one section header + one prose block on page 1.
    var blocks = new List<PdfBlock>
    {
        new(new string('a', 60), PageNumber: 1, Order: 1),
    };
    var headings = new List<PdfStructureItem> { new("section_header", "Monsters", 1, 1) };
    var blockExtractor = Substitute.For<IPdfBlockExtractor>();
    blockExtractor.ExtractBlocksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
        .Returns(Task.FromResult(new PdfExtraction(blocks, headings)));

    // No bookmarks — the fallback must engage instead of aborting.
    var bookmarkReader = Substitute.For<IPdfBookmarkReader>();
    bookmarkReader.ReadBookmarks(Arg.Any<string>()).Returns(new List<PdfBookmark>());

    var embedding = Substitute.For<IEmbeddingService>();
    embedding.EmbedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
        .Returns(ci => Task.FromResult<IReadOnlyList<float[]>>(
            ((IReadOnlyList<string>)ci[0]).Select(_ => new float[] { 0.1f }).ToList()));

    var sut = new BlockIngestionOrchestrator(
        tracker, bookmarkReader, blockExtractor, embedding,
        Substitute.For<IVectorStoreService>(), Substitute.For<IBm25CorpusStats>(),
        NullLogger<BlockIngestionOrchestrator>.Instance);

    await sut.IngestBlocksAsync(bookId);

    // Ingested via the fallback (not marked failed with the old bookmark error).
    await tracker.Received(1).MarkJsonIngestedAsync(bookId, Arg.Is<int>(n => n >= 1), Arg.Any<CancellationToken>());
    await tracker.DidNotReceive().MarkFailedAsync(bookId, Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "FullyQualifiedName~BlockIngestionOrchestratorTests"`
Expected: build FAILS (mock return types) / new test FAILS (orchestrator still aborts on empty bookmarks).

- [ ] **Step 3: Rewrite the orchestrator's extract + TOC section**

In `Features/Ingestion/BlockIngestionOrchestrator.cs`, replace the block spanning the bookmark guard through the `foreach` header (the current lines 50–69) with:

```csharp
            var extraction = await blockExtractor.ExtractBlocksAsync(record.FilePath, cancellationToken);

            var bookmarks = bookmarkReader.ReadBookmarks(record.FilePath);
            TocCategoryMap tocMap;
            if (bookmarks.Count > 0)
            {
                tocMap = new TocCategoryMap(BookmarkTocMapper.Map(bookmarks));
            }
            else
            {
                LogHeadingFallback(logger, record.DisplayName, recordId, extraction.Headings.Count);
                tocMap = new TocCategoryMap(
                    FullCoverageHeadingTocMapper.Map(extraction.Headings, record.DisplayName));
            }

            if (record.ChunkCount.HasValue)
                await vectorStore.DeleteBlocksByHashAsync(hash, cancellationToken);

            var version = Enum.TryParse<DndVersion>(record.Version, ignoreCase: true, out var v)
                ? v : DndVersion.Edition2014;

            var chunks = new List<BlockChunk>();
            var globalIndex = 0;
            foreach (var block in extraction.Blocks)
            {
```

- [ ] **Step 4: Update the empty-chunks failure message and remove the dead bookmark error**

Delete the `NoBookmarksError` constant (top of the class) and the now-unused `LogNoBookmarks` logger method. Replace the `chunks.Count == 0` failure block:

```csharp
            if (chunks.Count == 0)
            {
                LogNoBlocksMatched(logger, record.DisplayName, recordId);
                await tracker.MarkFailedAsync(recordId,
                    "No ingestable content found: the PDF produced no prose blocks in any section.",
                    CancellationToken.None);
                return;
            }
```

Add the new logger method alongside the others (near the bottom of the class):

```csharp
    [LoggerMessage(Level = LogLevel.Information,
        Message = "No bookmarks for {DisplayName} (id={Id}) — using heading-derived full-coverage TOC from {HeadingCount} headings")]
    private static partial void LogHeadingFallback(ILogger logger, string displayName, int id, int headingCount);
```

- [ ] **Step 5: Run the orchestrator tests to verify they pass**

Run: `dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "FullyQualifiedName~BlockIngestionOrchestratorTests"`
Expected: PASS (all, including the new fallback test and the unchanged MinerU-error test).

- [ ] **Step 6: Commit**

```bash
git add Features/Ingestion/BlockIngestionOrchestrator.cs DndMcpAICsharpFun.Tests/Ingestion/BlockIngestionOrchestratorTests.cs
git commit -m "feat(ingestion): bookmark-less block ingestion falls back to full-coverage heading TOC"
```

---

### Task 4: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Full build (warnings-as-errors)**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. In particular, confirm no unused-symbol warnings from the removed `NoBookmarksError` / `LogNoBookmarks`.

- [ ] **Step 2: Full test suite**

Run: `dotnet test --nologo`
Expected: all pass except the known-flaky `CombatServiceEndTests.EndCombat_writes_back_approved_hp_and_drops_breadcrumb` (passes in isolation; unrelated). If any *other* test fails, stop and investigate.

- [ ] **Step 3: DI-resolution sanity**

Run: `dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "FullyQualifiedName~FullContainerScopeValidation"`
Expected: PASS — the `IPdfBlockExtractor` change still resolves through DI.

- [ ] **Step 4: Update the openspec tasks checklist**

Mark the code tasks in `openspec/changes/bookmarkless-block-fallback/tasks.md` complete (groups 1–3 and the code items of group 4). Leave task 4.3 (live MPMM/EEPC re-ingest) unchecked — it is deferred and operational.

**DEFERRED (not part of this plan — do NOT do here):** rebuilding the app image and re-ingesting MPMM (id 8) / EEPC (id 9) live. A rebuild restarts the app and would kill the in-progress SCAG/MTF entity extraction. Run it only after that extraction completes. EEPC additionally needs its `SourceKey=null` story decided first.
