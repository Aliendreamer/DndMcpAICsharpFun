# Block-Ingestion Heading Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the bookmark-derived section TOC matches zero blocks, block ingestion self-heals by rebuilding chunks from the same-source MinerU heading TOC instead of marking the book `Failed`.

**Architecture:** `BlockIngestionOrchestrator.IngestBlocksAsync` currently builds a `TocCategoryMap` from PDF bookmarks (a different page-coordinate source than the MinerU blocks) and drops any block whose page misses every section. We extract the block→chunk loop into a pure, testable `BuildChunks` helper, call it with the bookmark TOC, and — only when that yields zero chunks and headings exist — rebuild with `FullCoverageHeadingTocMapper` (whose heading pages share the blocks' MinerU extraction, so they align). The final `Failed` guard stays and now runs only after the fallback also fails.

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions. Serena MCP for all `.cs` reads/edits.

## Global Constraints

- **Serena MANDATORY** for every `.cs` read/edit (`find_symbol`, `replace_symbol_body`, `insert_after_symbol`, `replace_content`); built-in Read/Edit/Write on code files is forbidden.
- **Warnings-as-errors**: `dotnet build` must report `0 Warning(s) 0 Error(s)`.
- **Trigger is exactly zero chunks.** No percentage/coverage threshold. A book that matches ≥1 block via bookmarks must produce byte-identical output (no re-embed, no metadata change).
- **No public-API / endpoint / config change** → do NOT touch `DndMcpAICsharpFun.http`, `dnd-mcp-api.insomnia.json`, `Config/appsettings.*`.
- Do **not** modify `BookmarkTocMapper`, `FullCoverageHeadingTocMapper`, or `TocCategoryMap` — only the orchestrator changes.
- Exact types (already verified):
  - `PdfBlock(string Text, int PageNumber, int Order)` — `Features.Ingestion.Pdf`
  - `PdfExtraction(IReadOnlyList<PdfBlock> Blocks, IReadOnlyList<PdfStructureItem> Headings)`
  - `BlockChunk(string Text, BlockMetadata Metadata)` / `BlockMetadata(SourceBook, Version, Category, SectionTitle, SectionStart, SectionEnd, PageNumber, BlockOrder, GlobalIndex, BookType=Unknown, SourceKey=null)` — `Features.VectorStore`
  - `TocCategoryMap` — `Features.Ingestion.Extraction`; `TocSectionEntry(string Title, ContentCategory? Category, int StartPage, int? EndPage = null)` — `Domain`
  - `IngestionRecord` — mutable class, `Features`/`Domain`; fields `DisplayName`, `Version` (string), `BookType`, `FivetoolsSourceKey`, `FilePath`, `FileHash`, `ChunkCount`
  - `MinBlockChars = 40`, `MaxBlockChars = 800`; helpers `SplitLongBlock` (public static), `IsMostlyNumeric` (private static)
- `InternalsVisibleTo="DndMcpAICsharpFun.Tests"` IS configured → `BuildChunks` may be `internal static`.

---

### Task 1: Extract `BuildChunks` helper + wire the zero-match heading fallback

**Files:**
- Modify: `Features/Ingestion/BlockIngestionOrchestrator.cs` (extract helper from `IngestBlocksAsync` ~lines 68–99; add fallback branch; add `LogBookmarkTocFallback`)
- Test: `DndMcpAICsharpFun.Tests/Ingestion/BlockIngestionChunkTests.cs` (new)

**Interfaces:**
- Produces: `internal static List<BlockChunk> BlockIngestionOrchestrator.BuildChunks(IReadOnlyList<PdfBlock> blocks, TocCategoryMap tocMap, IngestionRecord record, DndVersion version)`
- Consumes: existing `FullCoverageHeadingTocMapper.Map`, `TocCategoryMap`, `SplitLongBlock`, `IsMostlyNumeric`.

- [ ] **Step 1: Write the failing test**

Create `DndMcpAICsharpFun.Tests/Ingestion/BlockIngestionChunkTests.cs`:

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public sealed class BlockIngestionChunkTests
{
    private static IngestionRecord Record() => new()
    {
        DisplayName = "Test Book",
        Version = "Edition2014",
        BookType = BookType.Setting,
        FivetoolsSourceKey = "TST",
    };

    private static readonly IReadOnlyList<PdfBlock> Blocks =
    [
        new PdfBlock(new string('a', 60), PageNumber: 1, Order: 0),
        new PdfBlock(new string('b', 60), PageNumber: 2, Order: 1),
        new PdfBlock(new string('c', 60), PageNumber: 3, Order: 2),
    ];

    [Fact]
    public void BuildChunks_returns_empty_when_no_block_page_falls_in_any_section()
    {
        // Bookmark-derived TOC whose only section starts at page 100 — every block (pages 1-3) misses.
        var bookmarkToc = new TocCategoryMap([new TocSectionEntry("Chapter 1", ContentCategory.Rule, 100)]);

        var chunks = BlockIngestionOrchestrator.BuildChunks(Blocks, bookmarkToc, Record(), DndVersion.Edition2014);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void BuildChunks_produces_page_aligned_chunks_from_a_full_coverage_toc()
    {
        // Heading-derived TOC starting at page 1 covers every block.
        var headingToc = new TocCategoryMap([new TocSectionEntry("Welcome", ContentCategory.Rule, 1)]);

        var chunks = BlockIngestionOrchestrator.BuildChunks(Blocks, headingToc, Record(), DndVersion.Edition2014);

        chunks.Should().HaveCount(3);
        chunks.Should().OnlyContain(c => c.Metadata.SectionTitle == "Welcome");
        chunks.Select(c => c.Metadata.GlobalIndex).Should().Equal(0, 1, 2);
        chunks.Should().OnlyContain(c => c.Metadata.SourceKey == "TST");
    }
}
```

- [ ] **Step 2: Run the test — expect compile failure**

Run: `dotnet test --filter "FullyQualifiedName~BlockIngestionChunkTests"`
Expected: FAIL — `BuildChunks` does not exist.

- [ ] **Step 3: Extract `BuildChunks` (Serena)**

Add this `internal static` method to `BlockIngestionOrchestrator` (via `insert_after_symbol` on `SplitLongBlock`, or `insert_before_symbol` on `IsMostlyNumeric`). It is the current inline loop, verbatim, minus the per-block `cancellationToken.ThrowIfCancellationRequested()` (the helper is pure/CPU-bound; the outer embedding loop still checks cancellation per batch):

```csharp
internal static List<BlockChunk> BuildChunks(
    IReadOnlyList<PdfBlock> blocks,
    TocCategoryMap tocMap,
    IngestionRecord record,
    DndVersion version)
{
    var chunks = new List<BlockChunk>();
    var globalIndex = 0;
    foreach (var block in blocks)
    {
        if (block.Text.Length < MinBlockChars) continue;
        if (IsMostlyNumeric(block.Text)) continue;

        var entry = tocMap.GetEntry(block.PageNumber);
        if (entry is null) continue;

        foreach (var splitText in SplitLongBlock(block.Text))
        {
            var meta = new BlockMetadata(
                SourceBook: record.DisplayName,
                Version: version,
                Category: entry.Category ?? ContentCategory.Rule,
                SectionTitle: entry.Title,
                SectionStart: entry.StartPage,
                SectionEnd: entry.EndPage ?? int.MaxValue,
                PageNumber: block.PageNumber,
                BlockOrder: block.Order,
                GlobalIndex: globalIndex++,
                BookType: record.BookType,
                SourceKey: record.FivetoolsSourceKey);
            chunks.Add(new BlockChunk(splitText, meta));
        }
    }
    return chunks;
}
```

- [ ] **Step 4: Replace the inline loop in `IngestBlocksAsync` with a call + fallback (Serena)**

Replace the current block-building block (from `var chunks = new List<BlockChunk>();` through the closing `}` of the `foreach (var block in extraction.Blocks)` loop, i.e. the whole ~68–99 span) with:

```csharp
var chunks = BuildChunks(extraction.Blocks, tocMap, record, version);

// The bookmark TOC (from PdfPig) and the blocks (from MinerU) are independent page sources;
// when they don't align, every block misses its section and chunks is empty. Rebuild from the
// same-source heading TOC before giving up. Guarded on bookmarks.Count > 0 so the already
// bookmarkless path (tocMap is already heading-derived) never rebuilds redundantly.
if (chunks.Count == 0 && extraction.Headings.Count > 0 && bookmarks.Count > 0)
{
    LogBookmarkTocFallback(logger, record.DisplayName, recordId, extraction.Headings.Count);
    var headingTocMap = new TocCategoryMap(
        FullCoverageHeadingTocMapper.Map(extraction.Headings, record.DisplayName));
    chunks = BuildChunks(extraction.Blocks, headingTocMap, record, version);
}
```

Leave the existing `if (chunks.Count == 0) { LogNoBlocksMatched(...); await tracker.MarkFailedAsync(...); return; }` block exactly as-is immediately below — it is now the final guard after the fallback.

- [ ] **Step 5: Add the `LogBookmarkTocFallback` LoggerMessage (Serena)**

Insert next to the existing `LogHeadingFallback`/`LogNoBlocksMatched` partial declarations:

```csharp
[LoggerMessage(Level = LogLevel.Warning,
    Message = "Bookmark TOC matched 0 blocks for {DisplayName} (id={Id}) — falling back to heading-derived TOC from {HeadingCount} headings")]
private static partial void LogBookmarkTocFallback(ILogger logger, string displayName, int id, int headingCount);
```

- [ ] **Step 6: Run the tests — expect pass**

Run: `dotnet test --filter "FullyQualifiedName~BlockIngestionChunkTests"`
Expected: PASS (2/2).

- [ ] **Step 7: Build + format + non-persistence suite**

Run: `dotnet build` → `0 Warning(s) 0 Error(s)`; `dotnet format --verify-no-changes`; `dotnet test --filter "FullyQualifiedName!~Persistence"` → all green (confirms the `BuildChunks` extraction didn't regress the bookmark path).

- [ ] **Step 8: Commit**

```bash
git add Features/Ingestion/BlockIngestionOrchestrator.cs DndMcpAICsharpFun.Tests/Ingestion/BlockIngestionChunkTests.cs
git commit -m "fix(ingest): heading-TOC fallback when bookmark TOC matches zero blocks"
```

---

### Task 2: Live validation on VRGR + downstream unblock (data gate — no spec-code change)

**Files:** none (operational validation against the running container).

**Interfaces:** consumes the admin key from `docker exec dndmcpaicsharpfun-app-1 cat /app/Config/appsettings.Development.json` → `Admin.ApiKey`. All docker/curl need `dangerouslyDisableSandbox`.

- [ ] **Step 1: Rebuild the running app with the fix**

`docker compose -f docker-compose.yml up -d --build app` (NO `ADMIN_API_KEY` env var — keys come from the git-crypt appsettings). Wait for healthy.

- [ ] **Step 2: Reset VRGR out of `Failed` and re-ingest blocks**

VRGR (id 13) is status `2` (Failed). Re-run `POST /admin/books/13/ingest-blocks` (if a Failed status blocks re-enqueue, apply the stuck-status DB reset workaround from `mem:project_dev_container_staleness`). Watch `docker logs` for the new `Bookmark TOC matched 0 blocks ... falling back` warning.

- [ ] **Step 3: Confirm blocks landed**

`GET /admin/books` → id 13 shows `status` advanced (JsonIngested/next) and `chunkCount > 0` (expect thousands, given 3692 MinerU text items). Zero is a FAIL — investigate before proceeding.

- [ ] **Step 4: Confirm the corpus is queryable (deterministic, no qwen3/browser)**

`GET /retrieval/search` with `--data-urlencode "q=<Ravenloft topic e.g. Dark Powers dread domains darklord>"` and `--data-urlencode "sourceBook=Van Richten's Guide to Ravenloft"` → returns VRGR passages that did not exist pre-ingest. This proves ingest + the Ravenloft→VRGR setting scope end-to-end.

- [ ] **Step 5: Record outcome + hand off extraction**

Report chunkCount, the fallback-log line, and a sample retrieved passage. Note the follow-up: VRGR entity extraction (`extract-entities` → validated finish) can now run since blocks exist — restart the status loop if desired. No commit in this task.
