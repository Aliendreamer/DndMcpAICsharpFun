# Replace Docling with Marker

## Why

The marker-converter-spike (`data/spike/marker-vs-docling.md`) showed Marker beats Docling on every conversion-quality axis that drives our entity-name and RAG-block problems: true OCR garble halved (10.2% vs 21.2% of headings book-wide; 11% vs 25% on the worst slice), subtitle pollution eliminated (0 vs 17 polluted headings — the source of names like "BESTIAL SOUL 6th-level Path of the Beast f eature"), and better table reconstruction. The corpus is small (3–4 books), so the one-time ~2h GPU conversion per book is acceptable.

## What Changes

- **BREAKING (internal): full replacement** — Docling is removed entirely: `docling` compose service, `DoclingPdfConverter`, `DoclingHealthCheck`, `DoclingOptions`, and the `Docling` config section. No fallback path.
- The spike `MarkerPdfConverter` is promoted to the production converter: DI-registered, options-driven (`Marker` config section), resilient job polling, and a table-caption fix (Marker `SectionHeader` blocks matching dice-caption patterns like `d4 …` are demoted to text so they stop stealing section context — the cause of the Monster candidate drop).
- The `marker` compose service becomes a permanent first-class service: project-owned named volume for model cache (NOT the external aidoctorassistant volume used during the spike), `app` health/depends_on switches from docling to marker.
- Converter abstractions renamed to neutral names: `IDoclingPdfConverter` → `IPdfStructureConverter`, `DoclingDocument` → `PdfStructureDocument`, `DoclingItem` → `PdfStructureItem`, `DoclingDiskCache` → `PdfConversionDiskCache`.
- Conversion disk cache gains a converter discriminator in the cache file name so stale Docling-era caches are never served as Marker output.
- New heading de-spacing normalizer: letter-spaced caps headings (`ABER R ATIONS`, `OPTIONAL C LASS FEATURES`) are collapsed before scanner input, fixing the dominant residual garble pattern.
- Operational: re-convert and re-ingest blocks for the 3–4 registered books (hand-corrected canonical JSONs remain the source of truth for entities).
- `use_llm` stays available in the marker wrapper but unused by the app (out of scope).

## Capabilities

### New Capabilities

- `marker-pdf-conversion`: Marker-based PDF → structured items conversion (service contract, JSON block mapping, caption demotion, job polling, caching with converter discriminator)
- `heading-despacing`: Deterministic normalization of letter-spaced caps headings applied to structure items before candidate scanning

### Modified Capabilities

- `docling-pdf-extraction`: **REMOVED** — requirements retired wholesale, replaced by `marker-pdf-conversion`
- `ingestion-pipeline`: Block ingestion and entity extraction consume the renamed converter abstraction; Docling reachability requirement replaced by Marker health requirement

## Impact

- `Features/Ingestion/Pdf/*` (converter, cache, document records — renames + new converter), `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`, `Features/Ingestion/BlockIngestionOrchestrator.cs`
- `Infrastructure/Docling/*` removed; new `Infrastructure/Marker/` health check + options
- `docker-compose.yml` (docling service removed, marker service permanent + own volume), `docker/marker/`
- `Config/appsettings*.json` (`Docling` section → `Marker` section)
- `Extensions/ServiceCollectionExtensions.cs` (DI), `CLAUDE.md`, tests across `DndMcpAICsharpFun.Tests` that fake `IDoclingPdfConverter`
- Existing `data/docling-cache/` files become inert (discriminator prevents reuse); books re-converted on next ingest
