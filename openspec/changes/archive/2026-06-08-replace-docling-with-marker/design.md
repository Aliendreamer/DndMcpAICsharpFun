# Replace Docling with Marker — Design

## Context

The conversion layer turns book PDFs into `DoclingDocument(Markdown, Items)` where each item is `(Type, Text, PageNumber, Level)`. Two consumers: `BlockIngestionOrchestrator` (prose blocks → `dnd_blocks`) and `EntityExtractionOrchestrator` (heading-driven candidate scanning → canonical JSON). The spike (`data/spike/marker-vs-docling.md`, `openspec/changes/marker-converter-spike/`) proved Marker's structural superiority and produced a working `MarkerPdfConverter`, a marker FastAPI wrapper (`docker/marker/`), and a compose service wired to an external aidoctorassistant model volume.

Current spike-state leftovers this change must productionize or remove:

- `Features/Ingestion/Pdf/MarkerPdfConverter.cs` — spike-grade: static `HttpClient`, hardcoded container path constructor arg, no DI, no cache, infinite poll
- compose `marker` service marked spike, using `external: true` volume `aidoctorassistant_marker-models`
- `DndMcpAICsharpFun.Tests/Spike/MarkerVsDoclingComparisonTests.cs` — spike harness
- Docling: `DoclingPdfConverter`, `DoclingDiskCache` (wraps any `IDoclingPdfConverter`), `DoclingHealthCheck`, `DoclingOptions`, compose `docling` service

## Goals / Non-Goals

**Goals:**

1. Marker is the only converter; all Docling code, config, and the compose service are deleted.
2. Neutral abstraction names (`IPdfStructureConverter`, `PdfStructureDocument`, `PdfStructureItem`, `PdfConversionDiskCache`) so the next converter swap doesn't rename half the codebase.
3. Production-quality `MarkerPdfConverter`: `IHttpClientFactory`, `MarkerOptions` (`Url`, `PollIntervalSeconds`, `ConversionTimeoutMinutes`), bounded polling, container path mapping derived from config (`Ingestion:BooksPath`), meaningful errors.
4. Table-caption demotion in the JSON mapping: `SectionHeader` whose text matches `^d\d+\b` (dice captions) becomes a text item — fixes the Monster candidate drop.
5. Heading de-spacing normalizer applied to heading items at mapping time: collapse stray 1–2 letter all-caps fragments (`ABER R ATIONS` → `ABERRATIONS`, `OPTIONAL C LASS FEATURES` → `OPTIONAL CLASS FEATURES`).
6. Cache correctness: cache file name carries a converter discriminator (`<hash>.marker.json`); Docling-era `<hash>.json` files are ignored (and cleaned up opportunistically).
7. Project-owned model volume (`marker_models`, non-external); marker service healthy-gated under `app`.
8. Operational runbook step: re-convert + re-ingest blocks for the 3–4 registered books.

**Non-Goals:**

- `use_llm` table refinement (wrapper keeps the knob; app never sets it)
- Re-running entity extraction over already hand-corrected canonical JSONs
- Keeping any Docling fallback or config switch between converters
- 7b/vision model experiments

## Decisions

1. **Rename via serena `rename_symbol`** — mechanical, compiler-verified. Order: rename interface + records first, then delete Docling implementations, then wire Marker.
2. **Despacer is part of the mapping, not the scanner** — `MarkerPdfConverter` applies `HeadingDespacer.Normalize` to `section_header` item text. Scanner and orchestrators stay converter-agnostic. Despacer is a separate static class with its own tests (reusable if another converter returns).
3. **Despacing heuristic** (all-caps headings only): repeatedly merge a fragment of 1–2 uppercase letters with the adjacent fragment when the merged token is not a known standalone word (whitelist: `A`, `I`, `OF`, `TO`, `IN`, `ON`, `AT`, `BY`, `OR`, `AN`, `AS`, `IT`, `IS`, `BE`, `DO`, `NO`, `SO`, `UP`, `WE`, `D4`–`D20` dice tokens). Conservative: if ambiguous, leave unchanged — a spaced heading is recoverable downstream, a wrongly merged one is not.
4. **Cache discriminator in the file name** (`<sha256>.marker.json`) rather than wiping `data/docling-cache/`: zero risk, and the cache directory config key (`DoclingCacheDirectory` → renamed `ConversionCacheDirectory`) keeps pointing at the same folder.
5. **Marker job API stays async** (submit + poll): conversions take ~2h; the wrapper's in-memory job table is acceptable because `PdfConversionDiskCache` makes every conversion one-time per file hash. Poll interval 15s, timeout default 240 min.
6. **`app` compose dependency**: `marker: condition: service_healthy` with `start_period: 10m` (model load) replacing the docling dependency. Marker health check already exists from the spike.
7. **Spike harness test file is deleted** with the spike; the comparison reports under `data/spike/` stay as the decision record.

## Risks / Trade-offs

- **2h conversion on first ingest of a new book** — mitigated by disk cache + async ingestion endpoints already being fire-and-forget; runbook documents the expectation.
- **GPU contention with Ollama during combined ingest** (marker models + embedding/extraction models share 8 GB): block ingestion embeds after conversion completes, so phases serialize naturally; worst case Ollama offloads to CPU.
- **Despacer false merges** on unusual headings — mitigated by the conservative whitelist rule + unit tests over the real garble corpus from the spike report.
- **Marker wrapper restart loses in-flight jobs** — acceptable: conversion is retried on next ingest call; cache prevents repeat cost after success.
- **Markdown property consumers**: `PdfStructureDocument.Markdown` is item-concatenated for Marker (not pretty markdown). Verified consumers only use `Items`; `Markdown` retained for debugging only.
