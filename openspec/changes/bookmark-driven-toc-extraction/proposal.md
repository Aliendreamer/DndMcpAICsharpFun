## Why

The current TOC extraction reads the registered `tocPage` of the PDF, sends its raw text to an LLM, and asks the model to return a structured JSON list of section entries. In practice this fails on real D&D rulebooks: the PHB TOC is a multi-column layout, and PdfPig's text extraction interleaves columns into nonsense (`"CHAPTER Choosing a 2: Race RACES"`), so the LLM cannot recover the structure. Empirically the LLM returns a near-empty single object and `TocCategoryMap` is empty, causing every page to be skipped during extraction. Better prompts or larger models cannot fix this — the input itself is unrecoverable.

D&D rulebook PDFs (and almost all professionally-published PDFs) embed a structured bookmark/outline tree that *is* the TOC, with exact `(title, pageNumber)` pairs. Reading bookmarks gives us perfect ground truth, no LLM cost, no parse failures, and a much simpler pipeline.

## What Changes

- **BREAKING**: Remove the `tocPage` form field from `POST /admin/books/register`. Books are registered without specifying a TOC page.
- **BREAKING**: Drop the `TocPage` column from `IngestionRecords`. EF migration removes the column.
- **BREAKING**: Remove the `POST /admin/books/{id}/debug-toc` admin endpoint (added earlier this session, no longer applicable).
- Wire `IPdfBookmarkReader` (already implemented but currently orphaned) into DI.
- Fix `PdfPigBookmarkReader` to walk the bookmark tree recursively (current code only goes 2 levels deep).
- Add a `BookmarkTocMapper` that converts `IReadOnlyList<PdfBookmark>` to `IReadOnlyList<TocSectionEntry>`, assigning `ContentCategory` via title-keyword heuristics ("spell" → Spell, "monster"/"bestiary" → Monster, etc.).
- `IngestionOrchestrator.ExtractBookAsync` and `ExtractSinglePageAsync` use bookmarks as the source of section ranges; no LLM call for TOC.
- Delete `ITocMapExtractor`, `OllamaTocMapExtractor`, `TocMapDebugResult`, the corresponding tests, the DI registration, and the system prompt.
- Update `DndMcpAICsharpFun.http`, README references, and any spec/doc that mentions `tocPage` or the LLM TOC parser.
- Tests: add coverage for the bookmark walker (recursive), the bookmark→TOC mapper (heuristic categories, end-page derivation), and the orchestrator's bookmark-driven path.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `ingestion-pipeline`: Section discovery now reads the PDF bookmark tree instead of LLM-parsing a TOC page; the `tocPage` registration field is removed.
- `llm-extraction`: The TOC-map extractor and its prompt are removed. LLM usage in extraction is now strictly per-page entity extraction.
- `http-contracts`: `POST /admin/books/register` no longer accepts `tocPage`; `POST /admin/books/{id}/debug-toc` is removed.

## Impact

- **Code**: Deletions in `Features/Ingestion/Extraction/` (TOC extractor + interface + debug result). Additions in `Features/Ingestion/Pdf/BookmarkTocMapper.cs`. Modifications in `IngestionOrchestrator`, `BooksAdminEndpoints`, `IngestionRecord`, `ServiceCollectionExtensions`, plus a new EF migration that drops the `TocPage` column.
- **API**: Breaking change to `POST /admin/books/register` (drops `tocPage` form field) and removal of `POST /admin/books/{id}/debug-toc`.
- **Data**: Existing rows lose their `TocPage` column on migration. No data loss for actively-used columns.
- **Behavior**: Books whose PDFs have no embedded bookmarks will fail with a clear error rather than degrading silently. PDFs with bookmarks (the realistic D&D case) get accurate, deterministic section boundaries with no LLM cost.
- **Dependencies**: None added. `OllamaSharp` is still used by the per-page entity extractor.
- **Tests**: Old `OllamaTocMapExtractorTests` + the `RegisterBook_MissingTocPage_Returns400` test deleted; new tests added for bookmark walker recursion, heuristic category mapping, and the orchestrator's bookmark path.
