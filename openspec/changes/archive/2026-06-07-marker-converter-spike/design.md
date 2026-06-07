# Marker vs Docling Converter Spike — Design

> **Spike:** throwaway experiment. Code quality bar is "answers the question", not production. The deliverable is a comparison report, not a feature. Production migration (if any) gets its own openspec change afterwards.

**Goal:** Measure whether Marker produces better PDF→structured-text conversion than Docling for D&D books, using the metric that drives our quality problem: the share of entity-candidate names that the `ExtractionNeedsReview` heuristic flags as garbled.

**Question to answer:** Would switching converters cut Tasha's Cauldron of Everything's 39% needsReview rate (156/404 entities) substantially (target: under ~15%)?

**No LLM calls.** The comparison runs the deterministic half of the pipeline only (conversion → scanner inputs → candidate scan → name heuristic).

---

## Why this measures the right thing

Entity names come from converter heading items: `BuildScannerInputs` (EntityExtractionOrchestrator) promotes items whose `Type` starts with `section`/`heading`/`title` to section titles; the scanner groups blocks by section title; the title becomes `EntityCandidate.DisplayName` → `EntityEnvelope.Name`. `ExtractionNeedsReview.Derive` flags garbled names. So converter heading quality *is* the needsReview rate, no LLM needed.

`TocCategoryMap` is built from PDF bookmark TOC entries — identical for both converters — so candidate categorisation is held constant and the comparison isolates conversion quality.

## Test subject

- Book: **Tasha's Cauldron of Everything** — `books/4e8f1fe851c34c7db1e8a0d8e1bff02d.pdf` (IngestionRecord Id 2), worst current quality: 156/404 needsReview (39%).
- Docling side: served by the existing `docling` compose service; conversion is already in `data/docling-cache` (instant).
- Marker side: one fresh conversion (~30–60 min on CPU; one-time).

## Components

### 1. `marker` compose service (new, spike-scoped)

- `docker/marker/Dockerfile` + `docker/marker/app.py`, copied from `~/projects/aidoctor/docker/marker/` and modified:
  - `output_format: "json"` (block tree with per-page grouping and block types incl. `SectionHeader`) instead of `"markdown"`.
  - No LLM mode (`use_llm` stripped) — we are testing base conversion.
- Service on port `5002:5002`, `books/` volume mounted read-only.
- Reuses the same pip dependency set as aidoctor's wrapper (models download on first start).

### 2. `MarkerPdfConverter : IDoclingPdfConverter` (spike quality)

- `Features/Ingestion/Pdf/MarkerPdfConverter.cs`.
- POSTs the PDF to the marker service, polls the job endpoint, maps Marker JSON blocks → `DoclingDocument(Markdown, Items)`:
  - Marker `SectionHeader` blocks → `DoclingItem(Type: "section_header", Text, PageNumber, Level)`
  - other text-bearing blocks → `DoclingItem(Type: "text", Text, PageNumber, null)`
  - page number from Marker's per-page grouping.
- NOT registered in DI. No retries, minimal error handling. Constructed directly by the harness.

### 3. Comparison harness (manually-run xUnit test)

- `DndMcpAICsharpFun.Tests/Spike/MarkerVsDoclingComparisonTests.cs`, marked `[Fact(Skip = "spike — run manually")]` in the committed state; run locally with the Skip removed or via trait filter.
- Steps per converter:
  1. obtain `DoclingDocument` (Docling via existing converter + disk cache; Marker via `MarkerPdfConverter`)
  2. replicate `BuildScannerInputs` (20-line private method — duplicated in the harness, acceptable for a spike)
  3. build `TocCategoryMap` from the PDF bookmarks exactly as the orchestrator does
  4. `EntityCandidateScanner.Scan(...)`
  5. apply `ExtractionNeedsReview.Derive(name, confidence: null)` to every candidate name
- Output: `data/spike/marker-vs-docling.md` report with:
  - candidate count per `EntityType`, both converters
  - flagged-name count + rate, both converters
  - 20 worst sample names side by side (same TOC page → paired where possible)
  - 3 table-rendering samples (same page ranges) for eyeball comparison
  - headline verdict numbers

## Decision rule

| Outcome | Next step |
| --- | --- |
| Marker flagged-name rate substantially lower (≲15% vs 39%) and tables look no worse | Spec the real migration (config-switchable converter) as a new openspec change |
| Rates comparable or Marker worse | Delete spike code, keep the report in `data/spike/` for the record |
| Mixed | Discuss per-book converter selection |

## Out of scope

- LLM extraction runs (optional follow-up, separate decision)
- DI registration, config options, converter selection logic
- Docling removal, cache format changes, re-ingestion of any book
- Touching anything in `~/projects/aidoctor`

## Cleanup contract

Spike artifacts to delete if Marker loses: `docker/marker/`, the compose service block, `MarkerPdfConverter.cs`, the harness test. The report stays either way.
