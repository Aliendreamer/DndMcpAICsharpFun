## 1. Surface headings from the block extractor

- [x] 1.1 Add a `PdfExtraction(IReadOnlyList<PdfBlock> Blocks, IReadOnlyList<PdfStructureItem> Headings)` record (in `Features/Ingestion/Pdf/`).
- [x] 1.2 Change `IPdfBlockExtractor.ExtractBlocksAsync` to return `Task<PdfExtraction>` instead of `Task<IReadOnlyList<PdfBlock>>`.
- [x] 1.3 Update `StructureBlockExtractor.ExtractBlocksAsync` to collect `section_header` `doc.Items` into `Headings` (text + page number) in the same pass, returning `PdfExtraction`. Prose-block projection unchanged.
- [x] 1.4 Update `StructureBlockExtractorTests` for the new return shape; add a case asserting `section_header` items are surfaced and a case asserting an empty (non-null) headings list when there are none.

## 2. Full-coverage heading TOC mapper

- [x] 2.1 Add `FullCoverageHeadingTocMapper.Map(IReadOnlyList<PdfStructureItem> headings) -> IReadOnlyList<TocSectionEntry>` in `Features/Ingestion/Pdf/` — one entry per non-empty heading (title = heading text), carry-forward category (`HeadingCategoryClassifier.Guess` when confident, else last confident, else `Rule`), front-matter catch-all when the first heading starts after page 1, and a whole-book catch-all when the heading list is empty.
- [x] 2.2 Add `FullCoverageHeadingTocMapperTests`: full coverage / no gaps (assert `TocCategoryMap.GetEntry` is non-null for every page across the range), per-heading titles, carry-forward category (sub-headings inherit `Monster`), pre-confident headings default to `Rule`, front-matter catch-all, and empty-headings → single whole-book section.

## 3. Orchestrator fallback wiring

- [x] 3.1 In `BlockIngestionOrchestrator.IngestBlocksAsync`, replace the empty-bookmarks abort (the `NoBookmarksError` `MarkFailedAsync` branch) with: build the fallback `TocCategoryMap` from the extractor's `Headings` via `FullCoverageHeadingTocMapper`. Keep the bookmark path (`BookmarkTocMapper`) exactly as-is.
- [x] 3.2 Consume the new `PdfExtraction` return (blocks + headings) from a single `ExtractBlocksAsync` call; iterate `extraction.Blocks` as before.
- [x] 3.3 Keep the `chunks.Count == 0` guard as the terminal "no ingestable content" failure; give it a message distinct from the retired bookmark error. Remove the now-unused `NoBookmarksError` constant.
- [x] 3.4 Update `BlockIngestionOrchestratorTests`: bookmarked path unchanged (regression); empty-bookmarks path builds the fallback TOC and ingests with nothing dropped; no-prose path fails cleanly with the new message.

## 4. Verify

- [x] 4.1 `dotnet build` clean (warnings-as-errors) and `dotnet test` green for the touched projects.
- [x] 4.2 Confirm no other consumers of `IPdfBlockExtractor.ExtractBlocksAsync` remain uncompiled (DI registration at `Extensions/ServiceCollectionExtensions.cs` still resolves).
- [x] 4.3 Live-validated: MPMM (id 8) re-ingested via the fallback = 3505 blocks (log: "heading-derived full-coverage TOC from 1065 headings"). EEPC (id 9) re-ingest still pending its source-key decision (separate). BM25 corpus-consistency re-ingest flagged as a follow-up.
