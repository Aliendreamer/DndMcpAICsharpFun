## Why

Block ingestion hard-aborts when a PDF has no embedded bookmark outline (`BlockIngestionOrchestrator` returns `MarkFailedAsync(NoBookmarksError)`). Two freshly-registered official books — MPMM (id 8) and EEPC (id 9) — failed for exactly this reason, even though their content is fully structured. The extraction path already tolerates bookmark-less PDFs by deriving a TOC from MinerU heading items; block ingestion is the only path that still refuses. That inconsistency blocks otherwise-ingestable books from the retrieval corpus.

## What Changes

- Block ingestion no longer aborts on a missing bookmark outline. When bookmarks are absent, it derives a **full-coverage** section TOC from MinerU `section_header` items so **every prose block is retained** (nothing dropped).
- **BREAKING (internal interface):** `IPdfBlockExtractor.ExtractBlocksAsync` returns a new `PdfExtraction(Blocks, Headings)` record instead of `IReadOnlyList<PdfBlock>`, surfacing the `section_header` items the converter already produces (and currently discards) in the same single conversion.
- A new `FullCoverageHeadingTocMapper` builds the fallback TOC with a coverage guarantee — an entry per heading (fine-grained titles), carry-forward category from the last confident heading, a front-matter catch-all before the first heading, and a whole-book catch-all when there are no headings at all.
- The existing bookmarked path is untouched — identical behavior, zero regression risk.
- Out of scope (recorded, not implemented): EEPC's keyless `SourceKey=null` story, and the post-ingest BM25 corpus-consistency re-ingest pass.

## Capabilities

### New Capabilities

- `full-coverage-block-toc`: A full-coverage heading-derived TOC for block ingestion — every prose block on every page maps to a section (per-heading titles, carry-forward category, catch-all gap-fill), guaranteeing no block is silently dropped. Includes surfacing the block extractor's `section_header` structure items (from the single existing conversion) so the fallback TOC needs no re-conversion. Distinct from `heading-derived-toc-fallback`, which is intentionally sparse/confident-only for entity extraction.

### Modified Capabilities

- `block-ingestion`: On a bookmark-less PDF, ingestion falls back to the full-coverage heading TOC and ingests all blocks instead of failing; only a PDF with no prose at all fails, with a clearer message.

> Note: the `IPdfBlockExtractor` return-shape change (now surfacing headings) is captured under the new `full-coverage-block-toc` capability rather than as a `block-extraction` delta, because the `block-extraction` spec is already drifted from the code (it describes a retired PdfPig/`BoundingBox` implementation, not the current MinerU `StructureBlockExtractor`). Fixing that drift is out of scope; see design.md → Open Questions.

## Impact

- **Code:** `Features/Ingestion/Pdf/IPdfBlockExtractor.cs` (return type), `Features/Ingestion/Pdf/StructureBlockExtractor.cs` (collect headings), new `Features/Ingestion/Pdf/FullCoverageHeadingTocMapper.cs`, `Features/Ingestion/BlockIngestionOrchestrator.cs` (fallback branch replaces the abort), reuses `HeadingCategoryClassifier` and `TocCategoryMap` unchanged.
- **Tests:** `StructureBlockExtractorTests` and `BlockIngestionOrchestratorTests` updated for the new return shape; new `FullCoverageHeadingTocMapperTests`.
- **Reference-only, unchanged:** `HeadingTocMapper` (extraction path), `TocCategoryMap`, `HeadingCategoryClassifier`, `PdfBlock`, `PdfStructureDocument`.
- **Runtime beneficiaries:** MPMM (id 8) + EEPC (id 9) become block-ingestable (re-ingest is a later operational step, gated on the EEPC source-key decision).
- **No API/HTTP contract change** — the `/admin/books/{id}/ingest-blocks` route is unchanged; only its internal robustness improves.
