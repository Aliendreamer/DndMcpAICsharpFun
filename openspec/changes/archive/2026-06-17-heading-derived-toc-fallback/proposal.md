## Why

Entity extraction produces **zero candidates for any PDF that has no embedded bookmarks** — the SRD is the first book to hit this. `EntityExtractionOrchestrator` builds its `TocCategoryMap` only from PDF bookmarks (`BookmarkTocMapper.Map`). When a PDF lacks bookmarks the TOC is empty, the candidate scanner maps every section's page to `ContentCategory.Unknown`, and all candidates are dropped (`MapCategoryToEntityType(Unknown)` → `null` → skipped). The `PdfPigBookmarkReader` even logs *"falling back to all-categories extraction"*, but no such fallback exists. Marker already tags the structure with headings (the SRD has 2,178 `section_header` items with clean titles like "Races", "Barbarian", "Class Features"), so the category signal is present — it's just unused.

## What Changes

Add a deterministic heading-derived TOC fallback that fires **only when a PDF has no usable bookmarks**, reusing the existing keyword category classifier — **no new LLM usage**. Category recognition stays keyword-based exactly as today; qwen3 remains confined to the per-candidate extraction step, unchanged.

- Extract the existing private `BookmarkTocMapper.GuessCategory(title)` into a **shared** title→`ContentCategory` classifier so the bookmark path and the new heading path share one source of truth. No behavior change to the bookmark path.
- Add `HeadingTocMapper.Map(headings)` — mirror of `BookmarkTocMapper` consuming Marker's `section_header` `PdfStructureItem`s (`Text`, `PageNumber`, `Level`). It runs each heading title through the shared classifier and emits a **sparse** `TocSectionEntry` list containing only headings that resolve to a confident category (drops `Rule`/`Unknown`). Sparseness lets `TocCategoryMap`'s existing page-range propagation fill the gaps — everything between "Barbarian" and the next confident heading stays `Class`, exactly like sparse bookmarks do today.
- Wire into `EntityExtractionOrchestrator.ExtractAsync`: read bookmarks as today; if the resulting `TocCategoryMap` is empty, build the TOC from `doc.Items` `section_header` items via `HeadingTocMapper` instead. Fix the misleading log to name the real heading-derived fallback.

## Capabilities

### New Capabilities
- `heading-derived-toc-fallback`: When a book PDF has no embedded bookmarks, the extraction pipeline SHALL derive its `TocCategoryMap` from Marker's heading structure items using the same deterministic keyword classifier as the bookmark path, so bookmark-less books produce entity candidates instead of an empty result. Includes the sparse/confident-category emission rule, page-range propagation behavior, and the guarantee that bookmarked books are unaffected.

### Modified Capabilities
<!-- None. entity-extraction-pipeline's observable requirements are unchanged; this adds a new fallback capability rather than modifying existing extraction behavior. The bookmark path keeps byte-for-byte identical TOC output. -->
- _(none)_

## Impact

- **Code:** new `HeadingTocMapper` (Features/Ingestion/Pdf); `GuessCategory` extracted to a shared classifier reused by `BookmarkTocMapper`; one branch added in `EntityExtractionOrchestrator.ExtractAsync`; corrected log message in `PdfPigBookmarkReader`.
- **Behavior:** bookmark-less PDFs (SRD, future homebrew) now extract entities; **bookmarked books (PHB/DMG/MM/TCE) are completely unaffected** — the fallback only fires when bookmarks are absent.
- **No** new LLM calls, no schema changes, no API changes, no migrations.
- **Follow-on (out of scope):** re-running the SRD extraction once this lands is a separate operational step.
