## Why

Block ingestion builds its section TOC from PDF bookmarks (read by `PdfPigBookmarkReader`), but places each block using the page number from the MinerU block extractor — two independent page-coordinate sources. When a book's bookmark pages don't align with MinerU's page numbering, *every* block resolves to no section and is dropped, so the book is marked `Failed` with "No blocks matched any section" even though its conversion is healthy. Van Richten's Guide to Ravenloft (id 13) hit exactly this: a 4824-item / 1071-header MinerU conversion produced **zero** ingested blocks, blocking both retrieval and the entity-extraction pipeline for the whole book.

## What Changes

- When the bookmark-derived TOC yields **zero** embeddable chunks and the conversion has section-header items, the worker retries chunk-building once with a full-coverage heading TOC (`FullCoverageHeadingTocMapper`). Heading pages and block pages come from the *same* MinerU extraction, so they are guaranteed page-aligned.
- The book is marked `Failed` only if the heading fallback *also* yields zero chunks (a genuinely empty PDF).
- The trigger is **exactly zero chunks**, so any book that already matches ≥1 block via bookmarks is completely unaffected — no re-embedding, no section-title/category changes.
- A `LogBookmarkTocFallback` warning is emitted when the fallback engages, turning a previously silent cross-source page divergence into a visible signal.
- Internal refactor: extract a pure `BuildChunks(blocks, tocMap, record, version)` helper from `IngestBlocksAsync` so both the bookmark path and the heading fallback are unit-testable without embedding/Qdrant. No public API or endpoint change.

## Capabilities

### New Capabilities

<!-- none -->

### Modified Capabilities

- `block-ingestion`: the "Block ingestion is end-to-end without LLM extraction" requirement is extended so the heading-TOC fallback also fires when bookmarks are *present but their TOC matches zero blocks* (not only when the bookmark tree is empty/absent). A new scenario covers the misaligned-bookmark case.

## Impact

- Code: `Features/Ingestion/BlockIngestionOrchestrator.cs` (fallback branch + extracted `BuildChunks` helper + new log message). No change to `FullCoverageHeadingTocMapper`, `BookmarkTocMapper`, or `TocCategoryMap`.
- Tests: `DndMcpAICsharpFun.Tests` — unit tests for `BuildChunks` (bookmark hit path + zero-match fallback path).
- Data: re-ingest VRGR (id 13) live to confirm chunkCount > 0 and VRGR passages become retrievable; unblocks the downstream extraction pipeline.
- No endpoint, request/response, auth, or config change → `.http`/`.insomnia.json` untouched.
