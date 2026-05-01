## Context

The extractor currently calls `OllamaLlmEntityExtractor` for every `ContentCategory` on every page — 8 passes × ~53s each = potentially 7 minutes per page. Most pages belong to one category (or none), so 7 of the 8 calls are wasted. The LLM also hallucinates plausible-sounding entities when forced to fill a category template on an irrelevant page.

PDF files published by Wizards of the Coast and most third-party D&D publishers embed a bookmark outline (PDF outline tree) in the file. PdfPig already in the project exposes this via `PdfDocument.Bookmarks`. Each bookmark has a `Title` and a destination page.

## Goals / Non-Goals

**Goals:**
- Read PDF bookmarks once per extraction run to build a page-range → `ContentCategory?` map
- Use the LLM once per book to classify bookmark titles into `ContentCategory` values
- Reduce per-page LLM calls from 8 to at most 1 (0 for pages with no mapped category)
- Add `POST /admin/books/{id}/cancel-extract` to stop a running extraction cleanly
- On cancel: delete `extracted/{bookId}/` folder, reset status to `Pending`

**Non-Goals:**
- Persisting the TOC map across runs (ephemeral — recomputed each `/extract`)
- Handling scanned/image-based PDFs with no embedded bookmarks (fall back to current behaviour)
- Cancelling queued-but-not-started work items (only the actively running extraction)

## Decisions

### 1. LLM-based bookmark classification (not keyword matching)

The LLM is called once with the full bookmark title list and asked to produce a JSON map of `{ "title": "ContentCategory" }`. This handles arbitrary book structures (MM, DMG, AD&D, third-party) without maintaining a keyword list.

**Alternative considered:** Keyword/regex matching against known patterns ("spell", "monster", "bestiary"). Rejected because it requires ongoing maintenance and will miss unusual chapter names in non-WotC books.

### 2. Ephemeral TOC map (not persisted)

The page-range map is built in memory at the start of `ExtractBookAsync` and passed down to the per-page extractor dispatch. It is not stored in SQLite or on disk.

**Alternative considered:** Storing the map as a JSON sidecar file alongside extracted pages. Rejected to keep the feature simple — a re-run always recomputes from the source PDF, which is already on disk.

### 3. Fallback when no bookmarks present

If `PdfDocument.Bookmarks` returns an empty collection, extraction proceeds with the current all-categories behaviour. This ensures backwards compatibility with PDFs lacking an outline.

### 4. Per-job CancellationTokenSource in IngestionQueueWorker

The worker holds a `Dictionary<int, CancellationTokenSource>` keyed by book ID. When `ExtractBookAsync` starts, a new CTS is registered. The cancel endpoint resolves the CTS by book ID and calls `Cancel()`. On `OperationCanceledException`, the orchestrator cleans up and resets status.

**Alternative considered:** A global cancel-all mechanism. Rejected — too coarse, user needs per-book control.

## Risks / Trade-offs

- **LLM classification quality** — the LLM may misclassify an unusual chapter title (e.g. "The Weave of Magic" → Spell vs Rule). Mitigation: the map is ephemeral; a mis-classification shows up immediately in the extracted JSON files the user can now inspect in `./books/extracted/`.
- **Empty bookmark tree** — some PDFs have no embedded outline. Mitigation: fallback to all-categories as today; log a warning so the user knows.
- **Race condition on cancel** — if cancel arrives between pages, the cleanup may miss the page currently being written. Mitigation: cleanup deletes the entire `extracted/{bookId}/` directory, so any partial page file is removed regardless.

## Migration Plan

No data migration required. Existing extracted JSON files are unaffected. The change is additive — `/extract` gains smarter dispatch internally, and a new `/cancel-extract` endpoint is added.
