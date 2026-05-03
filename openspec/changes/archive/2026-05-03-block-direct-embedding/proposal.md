## Why

The current ingestion pipeline (LLM-extracted entities → JSON → embed → Qdrant) is slow on this hardware: per-page LLM calls × 300+ pages with `qwen2.5:7b` partially CPU-offloaded means hours per book. The LLM step is the bottleneck, and most of its value (typed entity fields like spell level, monster AC) is recoverable at *query time* by the consuming MCP agent — which is itself an LLM. For an MCP-backed RAG, raw block text plus bookmark-derived category/section metadata is often sufficient.

This change adds a **second, parallel ingestion path** that skips the LLM entirely: PdfPig's Docstrum block segmenter produces semantically-coherent text blocks with proper multi-column reading order, each block becomes a Qdrant point with bookmark-derived payload, and the embedding step runs straight from block text. We keep the existing pipeline untouched so we can A/B both approaches against real queries before deciding which to keep.

## What Changes

- Add a new Qdrant collection (default name `dnd_blocks`) alongside the existing `dnd_chunks`. The new collection stores points produced by the no-LLM path; the existing collection keeps its current contents.
- Add `IPdfBlockExtractor` (PdfPig + `DocstrumBoundingBoxes` + `UnsupervisedReadingOrderDetector`) that returns ordered text blocks per page with coordinates and page numbers.
- Add a new ingestion path `POST /admin/books/{id}/ingest-blocks` that runs end-to-end without LLM extraction: read bookmarks → map to sections → segment every PDF page into Docstrum blocks → enrich each block with bookmark-derived `(section_title, category, section_start, section_end)` payload → embed block text → upsert into the `dnd_blocks` collection. Single-stage, no JSON-on-disk intermediate.
- Add a configuration flag `Retrieval:Collection` (`"chunks"` | `"blocks"`, default `"chunks"`) so retrieval can be pointed at either collection without code changes. The existing `/retrieval/search` reads this flag.
- Update `QdrantCollectionInitializer` to create both collections at startup (same vector size, same payload indexes).
- Update `DndMcpAICsharpFun.http` to document the new endpoint and the collection-switch flag.
- Add tests for `IPdfBlockExtractor`, the block-ingest orchestrator path, and that retrieval honours the collection flag.

## Capabilities

### New Capabilities

- `block-extraction`: Layout-aware block segmentation of PDF pages using PdfPig's Docstrum + reading-order detectors, producing ordered text blocks with page coordinates that respect multi-column layouts.
- `block-ingestion`: A no-LLM ingestion path that turns a registered book directly into Qdrant points (one point per block) with bookmark-derived section/category payload, embedded in a single pass.

### Modified Capabilities

- `embedding-vector-store`: A second Qdrant collection is created and managed alongside the existing one. Retrieval gains a runtime knob to choose which collection it queries.
- `rag-retrieval`: `/retrieval/search` reads `Retrieval:Collection` and queries the corresponding collection. Defaults preserve current behaviour.
- `http-contracts`: New endpoint `POST /admin/books/{id}/ingest-blocks` documented in `.http`.
- `ingestion-pipeline`: New ingestion path coexists with the existing extract-then-ingest flow; neither replaces the other in this change.

## Impact

- **Code**: New `Features/Ingestion/Pdf/IPdfBlockExtractor.cs` + `PdfPigBlockExtractor.cs` + `PdfBlock.cs`. New `Features/Ingestion/BlockIngestionOrchestrator.cs` (or extension method on the existing orchestrator) that drives the no-LLM flow. Modifications in `BooksAdminEndpoints` (new route), `IngestionQueueWorker` (new work item type), `RetrievalOptions` / `QdrantOptions` (collection name flag), `QdrantCollectionInitializer` (initialise both), `RagRetrievalService` (read collection from config).
- **API**: Additive — `POST /admin/books/{id}/ingest-blocks` is new; nothing existing changes signature or removes. `/retrieval/search` behaviour switches based on config but its request/response shape is identical.
- **Configuration**: New `Retrieval:Collection` knob (string, default `"chunks"`). New `Qdrant:BlocksCollectionName` (string, default `"dnd_blocks"`).
- **Storage**: A second Qdrant collection, ~same size as `dnd_chunks` per book. Roughly 5-10× more points than the LLM path for the same book (block-level vs page-level), still small in absolute terms (~2-5k points per book).
- **Performance**: The block-ingest path should run in minutes (not hours) for a typical PHB-sized book on this hardware, because there are no LLM calls — only PdfPig segmentation (CPU, fast) and embedding (GPU, fast).
- **Behaviour**: No regressions to existing extraction/ingestion paths. Retrieval default behaviour unchanged unless `Retrieval:Collection` is explicitly set to `"blocks"`.
- **Tests**: New tests for `PdfPigBlockExtractor` (multi-column ordering, block boundaries), `BlockIngestionOrchestrator` (no-bookmark error, bookmark→category mapping, payload enrichment), and `RagRetrievalService` (collection flag honoured).
