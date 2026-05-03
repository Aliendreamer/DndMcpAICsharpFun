## Why

PdfPig's word extraction returns words in PDF content-stream order, which on multi-column books like the PHB is already interleaved across columns before any layout analysis runs. Both built-in PdfPig segmenters (`DocstrumBoundingBoxes` and `RecursiveXYCut`, A/B'd in the previous archived change) produce equivalently-scrambled block text because they preserve input order within each block. This caps retrieval quality regardless of how clever our chunking is — the consumer LLM gets text like *"bards to to sidelines / Many prefer stick the / in / their and / using magic inspire allies"* and has to reconstruct meaning from word soup.

A real fix needs layout analysis at the word level — detecting columns, ordering reading flow within columns, separating sidebars and stat blocks from body text. State-of-the-art open-source tools that do this (Docling, MarkerPDF, MinerU) are Python ML pipelines; they're not feasible to reimplement in C# in the project's lifetime. The pragmatic path is to run Docling as a small sidecar service in the existing Docker Compose stack and have our .NET app POST PDFs to it, receiving structured markdown back, then chunk and embed the markdown.

[Docling](https://github.com/DS4SD/docling) (IBM Research) is the choice: actively maintained, MIT-licensed, ships a `docling-serve` HTTP API container, outputs DocLayNet-classified markdown with section headings, paragraphs, tables, lists, captions, and code blocks all preserved with correct reading order. CPU-only inference is acceptable for our throughput needs (one PDF per ingestion, minutes per book). MarkerPDF stays as the tested-and-rejected backup if Docling has integration issues.

## What Changes

- Add a `docling` service to `docker-compose.yml` (and `.prod.yml`) running `quay.io/ds4sd/docling-serve-cpu:latest`. Exposes an HTTP API on port 5001 inside the Docker network. CPU-only — no GPU contention with Ollama.
- Add `Docling:BaseUrl` (default `http://docling:5001`) and `Docling:RequestTimeoutSeconds` (default 600) to a new `DoclingOptions` class.
- Add `IDoclingPdfConverter.ConvertAsync(string filePath) → Task<DoclingDocument>` in `Features/Ingestion/Pdf/`. Implementation `DoclingPdfConverter` POSTs the PDF to docling-serve's `/v1alpha/convert/file` endpoint and deserialises the JSON response into a typed `DoclingDocument` containing the markdown body and a list of structural blocks with their type, level, and source page.
- Add `IDoclingBlockExtractor : IPdfBlockExtractor` that calls `IDoclingPdfConverter`, walks the structural-block list (paragraphs, list items, headings, table cells), and yields `PdfBlock` records preserving page number and reading-order index. Headings are kept as their own blocks so retrieval can boost or filter on them later. Tables are flattened to their text content (Docling provides this directly).
- Extend `Ingestion:BlockSegmenter` to accept a third value `"docling"` that swaps in `IDoclingBlockExtractor` instead of `PdfPigBlockExtractor`. Default stays `"docstrum"` so existing deployments are unaffected.
- DI: register `DoclingPdfConverter` (Singleton, owns its `HttpClient`) and `DoclingBlockExtractor` (Singleton). Resolution of which `IPdfBlockExtractor` to inject becomes a small factory based on the `BlockSegmenter` value.
- Add health check `DoclingHealthCheck` that pings docling-serve `/health` so the app's readiness probe surfaces docling outages.
- Update `DndMcpAICsharpFun.http` with a comment naming the new `"docling"` value next to the existing `"docstrum"` / `"xycut"` mention.
- Tests: unit test `DoclingPdfConverter` with a mocked `HttpMessageHandler` returning canned JSON; unit test `DoclingBlockExtractor` over a fake `IDoclingPdfConverter`; integration test of the factory selection logic across all three segmenter values.

## Capabilities

### New Capabilities
- `docling-pdf-extraction`: a sidecar-driven PDF-to-blocks pipeline that replaces PdfPig+Docstrum when configured. Specifies the HTTP contract with docling-serve, the `DoclingDocument` shape we expect, and the mapping from Docling structural blocks to our `PdfBlock` records.

### Modified Capabilities
- `block-extraction`: gains the third `"docling"` value for `Ingestion:BlockSegmenter`. Selection of the underlying extractor is now a factory choice, not a fixed instance.
- `ingestion-pipeline`: documents the new compose service and the configuration knobs for it.
- `docker-stack`: adds the new `docling` service and updates the dependency graph (`app` depends on docling being healthy).
- `infrastructure-clients`: adds `IDoclingPdfConverter` next to the existing Ollama and Qdrant clients.

## Impact

- **Code**: ~250 lines net (HTTP client, DTOs, converter, extractor, factory, health check, tests). One small DI refactor for `IPdfBlockExtractor` to support multiple implementations.
- **Image size**: +1.5–2 GB for the docling-serve CPU image. One-time pull. The container itself is ~3-4 GB resident with the layout model loaded; it stays loaded and reuses across requests.
- **Memory**: +1-2 GB RAM for docling-serve (CPU inference). Fits comfortably in the user's 64 GB RAM.
- **Latency**: ~10-30 s per page on CPU for layout analysis. PHB-sized book (~310 pages) should finish in 5-15 minutes — comparable to or faster than the current PdfPig path because we no longer need per-page entity LLM calls.
- **API**: no change. `POST /admin/books/{id}/ingest-blocks` keeps the same contract.
- **Storage / migration**: re-ingestion produces new `PdfBlock` records with cleaner text. Existing `dnd_blocks` points are overwritten by deterministic point IDs (same `fileHash + globalIndex` derivation). No Qdrant schema change.
- **Operational**: new dependency in the stack. If docling-serve is down and the user has set `BlockSegmenter=docling`, ingestion fails with a clear error directing the operator to docling-serve logs. Falling back to `docstrum` requires a config change + restart.
- **Risk**: docling-serve is third-party. We pin a tagged image rather than `:latest` once we verify a working version. The conversion endpoint is the hot path; everything else is housekeeping.
