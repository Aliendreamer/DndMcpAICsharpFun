## Context

`BlockIngestionOrchestrator.IngestBlocksAsync` builds a `TocCategoryMap` and places each MinerU-extracted block into a section via `tocMap.GetEntry(block.PageNumber)`. A null entry means "no section" and the block is dropped (`continue`). The TOC is built from PDF bookmarks (`BookmarkTocMapper.Map(bookmarks)`) when `bookmarks.Count > 0`, otherwise from MinerU headings (`FullCoverageHeadingTocMapper.Map(extraction.Headings, …)`).

Bookmarks come from `PdfPigBookmarkReader` (reads the PDF directly); blocks and headings both come from the MinerU block extractor. These are two independent page-coordinate sources. When bookmark page numbers don't share MinerU's page numbering, every block's page falls outside every bookmark section → all blocks drop → `chunks.Count == 0` → the record is marked `Failed` ("No blocks matched any section"). VRGR (id 13) reproduced this: healthy 4824-item / 1071-header conversion, zero ingested blocks.

The heading TOC uses `extraction.Headings`, which share the *same* MinerU extraction (and therefore the same page numbering) as `extraction.Blocks`, so a heading-derived TOC is inherently page-aligned with the blocks.

## Goals / Non-Goals

**Goals:**
- A book whose bookmark TOC matches zero blocks self-heals by falling back to the same-source heading TOC, instead of failing.
- Zero behavioral change for every book that already ingests ≥1 block via bookmarks.
- Make the fallback observable (a warning log when it engages).
- Make the chunk-building logic unit-testable without embedding/Qdrant.

**Non-Goals:**
- No change to `BookmarkTocMapper`, `FullCoverageHeadingTocMapper`, or `TocCategoryMap` — the mappers are correct; only the *decision* of which TOC to use changes.
- No partial-coverage / percentage threshold (explicitly rejected in brainstorming for regression risk). Trigger is exactly zero chunks.
- No fix to the bookmark page-number mismatch itself (the fallback sidesteps it; a true bookmark-page-alignment fix is a separate, larger effort).
- No endpoint, request/response, auth, or config change.

## Decisions

**1. Extract a pure `BuildChunks` helper.** Refactor the block→chunk loop (lines ~68–99 of the current method: min-length filter, `IsMostlyNumeric` filter, `tocMap.GetEntry`, `SplitLongBlock`, metadata assembly) into:

```
internal static List<BlockChunk> BuildChunks(
    IReadOnlyList<ExtractedBlock> blocks, TocCategoryMap tocMap,
    IngestionRecord record, DndVersion version)
```

It is pure (no I/O, no logging), returns the chunk list, and is called once with the bookmark TOC and — only when that returns empty — again with the heading TOC. `globalIndex` is assigned inside the helper starting at 0; because only one of the two results is ever used downstream, indices stay contiguous. (Use the real element type of `extraction.Blocks` and the real `record`/metadata field names when implementing — the signature above is indicative.)

**2. Fallback branch in the orchestrator.** After the first `BuildChunks(bookmarkTocMap, …)`:

```
if (chunks.Count == 0 && extraction.Headings.Count > 0 && bookmarks.Count > 0)
{
    LogBookmarkTocFallback(logger, record.DisplayName, recordId, extraction.Headings.Count);
    var headingTocMap = new TocCategoryMap(
        FullCoverageHeadingTocMapper.Map(extraction.Headings, record.DisplayName));
    chunks = BuildChunks(extraction.Blocks, headingTocMap, record, version);
}
```

The `bookmarks.Count > 0` guard means this branch is unreachable on the already-bookmarkless path (which built the heading TOC up front) — it fires only for the "bookmarks present but misaligned" case, the exact VRGR gap. The existing `if (chunks.Count == 0)` → `MarkFailedAsync` block stays and now runs only after the fallback has also failed.

**3. New log message.** Add a `LoggerMessage`-generated `LogBookmarkTocFallback(ILogger, string displayName, int id, int headingCount)` at `Warning` level: "Bookmark TOC matched 0 blocks for {DisplayName} (id={Id}) — falling back to heading-derived TOC from {HeadingCount} headings". This mirrors the existing `LogHeadingFallback` style.

## Risks / Trade-offs

- **A book with genuinely mismatched bookmarks now gets heading-derived section titles/categories instead of the curated bookmark ones.** Accepted: any content is strictly better than a hard `Failed`, and this only affects books that currently ingest *nothing*.
- **The heading fallback could also mis-page in theory.** Not observed — headings and blocks share one MinerU extraction. If it ever yields zero too, the existing `Failed` path still catches it (unchanged final behavior).
- **Refactor risk in `BuildChunks` extraction.** Mitigated by the bookmark-happy-path unit test (proves the extracted helper produces the same chunks the inline loop did) plus the live VRGR re-ingest validation gate.
