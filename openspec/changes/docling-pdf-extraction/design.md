## Context

The previous experiment (`archive/2026-05-03-try-recursive-xycut-segmenter/`) confirmed that PdfPig's word ordering is the upstream cause of multi-column scrambling in our blocks. Both built-in segmenters preserve input word order and so produce equivalently-bad text on PHB-style two-column layouts. A real fix needs layout analysis at the word level.

[Docling](https://github.com/DS4SD/docling) is IBM Research's open-source document-conversion pipeline, MIT-licensed. It runs an ML model trained on DocLayNet to classify page regions (heading, paragraph, list, table, caption, figure, footer, etc.), then extracts text in correct reading order, including across columns and across pages. It exposes a Python library and an HTTP API server (`docling-serve`) that the IBM team distributes as a container image.

We integrate as a sidecar Docker service rather than embedding Python into the .NET app. The .NET ingestion path POSTs the registered PDF to docling-serve and receives a structured response containing per-page text-flow plus a hierarchical list of "items" (blocks with type and level). We map those items to our existing `PdfBlock` record and feed them through the rest of the unchanged ingestion pipeline (bookmarks → category → embed → Qdrant).

## Goals / Non-Goals

**Goals:**
- Eliminate multi-column word scrambling for PHB-style books.
- Preserve all existing ingestion APIs and Qdrant payload shape so retrieval is unaffected.
- Run docling-serve CPU-only to avoid contending with Ollama for the user's 8 GB VRAM.
- Make Docling opt-in via the existing `Ingestion:BlockSegmenter` knob (default stays `"docstrum"` so deployments not opting in see no change).
- Treat docling-serve as a normal service in the stack: health-checked, restart-policied, dependent service for `app`.

**Non-Goals:**
- Removing PdfPig entirely. The block extractor abstraction stays; PdfPig is still the fallback for unusual PDFs and the default for deployments not opting in.
- Replacing the bookmark-driven section/category mapping. Docling produces section headings of its own, but our `BookmarkTocMapper` already does this job and feeds the same `TocCategoryMap` shape; we don't need two sources of truth for sections.
- Embedding-model changes. Those are a separate decision (see the parking-lot note in the previous xycut experiment).
- Switching to MinerU or MarkerPDF in this change. They're acceptable backups if Docling fails integration, but only one tool is integrated here.
- Embedding Docling's table-structure metadata into Qdrant payload. Tables flatten to text. Surfacing structured tables is a future change for a frontend.

## Decisions

**1. Docling as a sidecar HTTP service, not a Python module embedded in the .NET process.**
Alternatives considered: shelling out to a Python CLI per ingestion; using IronPython / Python.NET. Both rejected — sidecar fits the existing Docker Compose pattern, isolates the ML dependency tree, and reuses the model load across requests (a CLI would reload per call).

**2. CPU-only inference.**
The user has 8 GB VRAM that Ollama needs for the embedding model. Docling on GPU would force VRAM swapping or contention. CPU-only is slower per page but acceptable: ~10-30 s/page × 310 pages ≈ 5-15 minutes per book, still vastly better than the LLM-extraction path that took hours.

**3. Pin a tagged image.**
We will not use `:latest`. After verifying a working version (current candidate: a tagged release of `docling-serve-cpu`), pin the digest in compose. Image upgrades are a deliberate change.

**4. New value on existing `Ingestion:BlockSegmenter`, not a separate knob.**
`"docling"` joins `"docstrum"` and `"xycut"` as a third option. The factory at DI resolution time picks one of three `IPdfBlockExtractor` implementations. Alternative considered: a separate top-level `Ingestion:Mode` flag. Rejected because the conceptual axis is the same — "which thing produces blocks for the embedding step?"

**5. Map Docling items to PdfBlock 1:1, preserving Docling's reading-order index.**
Docling's response contains a list of items in reading order; we walk it and emit a `PdfBlock` per item with its source page number and a monotonic order index. We do *not* attempt to re-merge or re-split. Docling's chunks are good chunks; the existing fragment-and-numeric-table filters in `BlockIngestionOrchestrator` still apply downstream.

**6. Headings stay as separate blocks, not merged into the next paragraph.**
Some chunkers concatenate a heading with its first paragraph. We don't, because headings carry their own retrieval value (a query for `"Channel Divinity"` wants to land on the heading even when its body is far away in the same page) and Docling assigns them a distinct item type that we can later filter on by adding a `block_type` payload field if useful.

**7. Health check, not just `depends_on`.**
docling-serve takes 30-60 s to start (model load). `app.depends_on.docling.condition: service_healthy` blocks app startup until docling responds. The health check is `wget -qO- http://localhost:5001/health` with a `start_period` that allows for first-load latency.

**8. Persistent error → mark Failed, not retry.**
If docling-serve returns 5xx or times out, the orchestrator marks the record `Failed` with the docling error message and does not retry. The user re-issues `POST /ingest-blocks` after fixing the underlying issue. Same shape as the existing "no bookmarks" failure mode.

## Risks / Trade-offs

- **[Risk]** docling-serve image is large (~3-4 GB). First boot adds ~5 minutes for image pull. → Acceptable. Cached layer thereafter.
- **[Risk]** Docling's heading detection may disagree with the PDF's bookmark tree. We trust bookmarks for category mapping; Docling's headings are informational. → Mitigated by the architectural choice in Decision 5: bookmarks remain the source of truth for section metadata; Docling-detected headings are blocks with their own retrieval value but do not re-derive section ranges.
- **[Risk]** Docling layout model has its own failure modes on weird PDFs (rotated pages, scanned-without-OCR pages, exotic fonts). → Mitigated by the fallback knob: an operator who hits a problem PDF can flip `Ingestion:BlockSegmenter` back to `"docstrum"` and re-ingest.
- **[Risk]** Docling-serve API may evolve and break our DTOs. → Mitigated by pinning a tagged image and writing focused integration tests against the response shape we depend on. Upgrades become a deliberate review.
- **[Trade-off]** Stack now has six containers (qdrant, ollama, ollama-pull, app, prometheus, grafana, docling) instead of five. Marginal additional ops cost. → Acceptable; this is the tool that does the job.
- **[Trade-off]** CPU inference is slow. Re-ingesting a 310-page book takes 5-15 minutes vs PdfPig's 1-2 minutes. → Acceptable for a one-shot per book. Quality lift dwarfs the latency cost.
- **[Trade-off]** Docling's tables-as-flat-text loses cell structure. → Acceptable for the LLM consumer; structured-table retrieval is a future feature.

## Migration Plan

1. Apply the change on a feature branch.
2. `docker compose pull docling` to fetch the image.
3. `docker compose up -d` — both `docling` and `app` come up; app waits for docling health.
4. Set `Ingestion__BlockSegmenter=docling` in the app environment (compose override or `appsettings.Development.json`).
5. Re-register a test book (or use existing).
6. `POST /admin/books/{id}/ingest-blocks` and watch logs for "Docling conversion …".
7. Run probe queries; compare against the existing Docstrum baseline. If clearly better, flip the default in `appsettings.json` to `"docling"` in a follow-up commit.

Rollback: revert the merge commit. The `docling` compose service is gone, factory falls back to PdfPig-only. `Ingestion__BlockSegmenter=docling` becomes invalid → warning + fallback to docstrum. No data loss; existing `dnd_blocks` content remains.
