## Why

The current PDF text extractor groups words by Y-coordinate, which interleaves multi-column D&D page layouts and produces garbled text that causes the LLM to misclassify entities (e.g., Bear Totem class features extracted as Monster stat blocks). The LLM also receives no heading hierarchy, so it cannot distinguish a section header from body content. Better-structured input and a stronger embedding model will significantly improve both extraction quality and retrieval relevance.

## What Changes

- **BREAKING** Replace `IPdfTextExtractor` / `PdfPigTextExtractor` with `IPdfStructuredExtractor` / `PdfPigStructuredExtractor` using DocstrumBoundingBoxes + UnsupervisedReadingOrderDetector
- **BREAKING** Enrich `extracted/<bookId>/page_<n>.json` from a bare entity array to `{ page, raw_text, blocks, entities }` — old files must be deleted and books re-extracted
- LLM entity extraction prompt receives `[H2]/[H3]/body`-formatted block text instead of flat string
- New synchronous admin endpoint `POST /admin/books/{id}/extract-page/{pageNumber}` for per-page extraction testing
- Upgrade embedding model from `nomic-embed-text` (768 dims) to `mxbai-embed-large` (1024 dims) — **BREAKING**: Qdrant collection must be recreated, all books re-ingested
- Add `PageEnd` (nullable int) to `ChunkMetadata` and Qdrant payload, tracked during the merge pass for multi-page entities

## Capabilities

### New Capabilities

- `structured-pdf-extractor`: DocstrumBoundingBoxes page segmentation with reading-order detection and font-size heading inference, outputting ordered blocks per page
- `enriched-page-json`: Per-page JSON format carrying both structured blocks and extracted entities in a single file
- `page-extract-endpoint`: Synchronous admin endpoint for extracting and inspecting a single page without persisting

### Modified Capabilities

- `ingestion-pipeline`: Extraction stage receives structured block text instead of flat page text; merge pass now tracks `PageEnd` across partial entity chains
- `llm-extraction`: Prompt input changes from raw string to `[H2]/[H3]/body`-formatted structured text
- `embedding-vector-store`: Model upgraded to `mxbai-embed-large` (1024 dims); `ChunkMetadata` gains `PageEnd` field; Qdrant payload gains `page_end`

## Impact

- `Features/Ingestion/Pdf/` — new extractor replaces existing
- `Features/Ingestion/Extraction/EntityJsonStore.cs` — new JSON schema, no backwards compatibility
- `Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs` — prompt input format change
- `Features/Ingestion/IngestionOrchestrator.cs` — wires new extractor
- `Features/Admin/BooksAdminEndpoints.cs` — new extract-page endpoint
- `Domain/ChunkMetadata.cs` — `PageEnd` field added
- `Infrastructure/Qdrant/QdrantPayloadFields.cs` — `page_end` constant added
- `Infrastructure/Qdrant/QdrantVectorStoreService.cs` — stores `page_end` payload
- `Config/appsettings.json` — `EmbeddingModel` and `VectorSize` updated
- `docker-compose.yml` — comment noting volume wipe required on `VectorSize` change
- PdfPig NuGet: `DocstrumBoundingBoxes` and `UnsupervisedReadingOrderDetector` already in `UglyToad.PdfPig` — no new packages needed
