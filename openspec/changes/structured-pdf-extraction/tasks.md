## 1. Structured PDF Extractor

- [ ] 1.1 Add `StructuredPage` and `PageBlock` domain records to `Domain/`
- [ ] 1.2 Define `IPdfStructuredExtractor` interface returning `IEnumerable<StructuredPage>`
- [ ] 1.3 Implement `PdfPigStructuredExtractor` using `DocstrumBoundingBoxes` and `UnsupervisedReadingOrderDetector`
- [ ] 1.4 Add font-size heading inference (h1/h2/h3/body) from median letter font size per block
- [ ] 1.5 Set `RawText` as ordered block texts joined with newlines
- [ ] 1.6 Register `IPdfStructuredExtractor` / `PdfPigStructuredExtractor` in DI; remove `IPdfTextExtractor` registration

## 2. Enriched Page JSON Format

- [ ] 2.1 Update `EntityJsonStore.SavePageAsync` to write `{ page, raw_text, blocks, entities }` object format
- [ ] 2.2 Update `EntityJsonStore.LoadAllPagesAsync` to read from the `entities` array in the new format
- [ ] 2.3 Update `EntityJsonStore.RunMergePassAsync` to read/write new format
- [ ] 2.4 Remove all old bare-array read/write paths from `EntityJsonStore`

## 3. LLM Extraction Prompt Input

- [ ] 3.1 Add `BuildPromptText(IReadOnlyList<PageBlock> blocks)` helper that formats blocks as `[H1]/[H2]/[H3]/body` lines
- [ ] 3.2 Update `OllamaLlmEntityExtractor.ExtractAsync` to accept and use `StructuredPage` instead of raw `pageText` string
- [ ] 3.3 Update `ILlmEntityExtractor` interface signature accordingly
- [ ] 3.4 Update `IngestionOrchestrator` to pass `StructuredPage` through the extraction pipeline

## 4. Ingestion Orchestrator Wiring

- [ ] 4.1 Replace `IPdfTextExtractor` with `IPdfStructuredExtractor` in `IngestionOrchestrator`
- [ ] 4.2 Pass `StructuredPage.Blocks` to `EntityJsonStore.SavePageAsync` for block storage
- [ ] 4.3 Update `ILlmClassifier` call site to use `StructuredPage.RawText` for classification pass

## 5. PageEnd Tracking

- [ ] 5.1 Add nullable `PageEnd` field to `ChunkMetadata` record
- [ ] 5.2 Update `EntityJsonStore.RunMergePassAsync` to record the last page of each merged partial chain as `PageEnd`
- [ ] 5.3 Cap `PageEnd` at the TOC chapter end page using `TocCategoryMap`
- [ ] 5.4 Add `page_end` constant to `QdrantPayloadFields`
- [ ] 5.5 Update `QdrantVectorStoreService` to include `page_end` in payload (omit field when null)
- [ ] 5.6 Add `page_end` integer index to `QdrantCollectionInitializer`
- [ ] 5.7 Update `JsonIngestionPipeline` to propagate `PageEnd` into `ChunkMetadata`

## 6. Single-Page Extract Endpoint

- [ ] 6.1 Add `POST /admin/books/{id}/extract-page/{pageNumber}` to `BooksAdminEndpoints`
- [ ] 6.2 Implement inline extraction: open PDF, extract single page, run classify + extract passes, return enriched page object
- [ ] 6.3 Add `?save=true` query parameter support to optionally persist to `extracted/<bookId>/page_<n>.json`
- [ ] 6.4 Return HTTP 400 if `pageNumber` exceeds PDF page count
- [ ] 6.5 Update `DndMcpAICsharpFun.http` with example request for new endpoint

## 7. Embedding Model Upgrade

- [ ] 7.1 Update `Config/appsettings.json`: set `Ollama:EmbeddingModel` to `mxbai-embed-large` and `Qdrant:VectorSize` to `1024`
- [ ] 7.2 Update `docker-compose.yml` ollama-pull init container to pull `mxbai-embed-large`
- [ ] 7.3 Add comment to `docker-compose.yml` noting that `qdrant_data` volume must be wiped when `VectorSize` changes

## 8. Tests

- [ ] 8.1 Unit test `PdfPigStructuredExtractor` heading inference with mocked font-size data
- [ ] 8.2 Unit test `BuildPromptText` formatting for h1/h2/h3/body blocks
- [ ] 8.3 Update `OllamaLlmEntityExtractorTests` for new `StructuredPage` input parameter
- [ ] 8.4 Update `EntityJsonStore` tests for new enriched JSON format
- [ ] 8.5 Unit test merge pass `PageEnd` tracking for two-page and three-page chains
- [ ] 8.6 Unit test `PageEnd` TOC chapter boundary cap
- [ ] 8.7 Integration test `POST /admin/books/{id}/extract-page/{pageNumber}` returns enriched JSON
- [ ] 8.8 Integration test `?save=true` persists file to disk
