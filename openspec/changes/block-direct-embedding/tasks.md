# Implementation Tasks

## 1. Block extractor

- [ ] 1.1 Add `Features/Ingestion/Pdf/PdfBlock.cs` — `sealed record PdfBlock(string Text, int PageNumber, int Order, PdfRectangle BoundingBox)`.
- [ ] 1.2 Add `Features/Ingestion/Pdf/IPdfBlockExtractor.cs` — interface with `IEnumerable<PdfBlock> ExtractBlocks(string filePath)`.
- [ ] 1.3 Add `Features/Ingestion/Pdf/PdfPigBlockExtractor.cs` — implementation using `PdfDocument.Open`, `NearestNeighbourWordExtractor.Instance`, `DocstrumBoundingBoxes.Instance.GetBlocks(words)`, and `UnsupervisedReadingOrderDetector.Instance.Get(blocks)` per page; trims whitespace-only blocks; assigns `Order` index per page.
- [ ] 1.4 Register `IPdfBlockExtractor → PdfPigBlockExtractor` as a singleton in `Extensions/ServiceCollectionExtensions.cs::AddIngestionPipeline`.
- [ ] 1.5 Add `DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigBlockExtractorTests.cs` — covers: multi-column reading order, whitespace-only block exclusion, page-number assignment, image-only page returns empty.

## 2. Qdrant: second collection

- [ ] 2.1 Add `Qdrant:BlocksCollectionName` to `QdrantOptions` (default `"dnd_blocks"`).
- [ ] 2.2 Update `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` to create both collections in sequence using the same vector size and distance metric. Apply the existing keyword/integer indexes to both, plus add a new integer index `block_order` to the blocks collection (and add the constant `QdrantPayloadFields.BlockOrder = "block_order"`).
- [ ] 2.3 Update `Infrastructure/Qdrant/QdrantPayloadFields.cs` with the new `BlockOrder` constant.
- [ ] 2.4 Add the matching `Config/appsettings.json` default values: `Qdrant:BlocksCollectionName: "dnd_blocks"` and `Retrieval:Collection: "chunks"`.

## 3. Block-ingestion orchestrator path

- [ ] 3.1 Add `Domain/IngestionWorkType` value `IngestBlocks` (extend the existing enum used by `IngestionWorkItem`).
- [ ] 3.2 Add `Features/Ingestion/IBlockIngestionOrchestrator.cs` — `Task IngestBlocksAsync(int recordId, CancellationToken ct = default)`.
- [ ] 3.3 Add `Features/Ingestion/BlockIngestionOrchestrator.cs` that: looks up the record, computes/persists hash, reads bookmarks (fail-fast with clear message if empty), maps to `TocCategoryMap`, calls `IPdfBlockExtractor.ExtractBlocks`, joins blocks with sections via `TocCategoryMap.GetEntry(block.PageNumber)`, embeds block text via `IEmbeddingService`, and upserts via a new method on `IVectorStoreService` that targets the blocks collection name.
- [ ] 3.4 Add `IVectorStoreService.UpsertBlocksAsync(IList<BlockChunk> chunks, string fileHash, CancellationToken ct)` and a corresponding `BlockChunk(string Text, BlockMetadata Metadata)` record. `BlockMetadata` carries the same fields as `ChunkMetadata` plus `int BlockOrder`.
- [ ] 3.5 Implement the new method on `QdrantVectorStoreService` that writes to the configured `BlocksCollectionName`. Point id is a deterministic GUID derived from `fileHash` and a stable `(page, blockOrder)` index.
- [ ] 3.6 Register `IBlockIngestionOrchestrator → BlockIngestionOrchestrator` (Scoped) in DI.
- [ ] 3.7 Update `IngestionQueueWorker` to dispatch `IngestionWorkType.IngestBlocks` to the new orchestrator.

## 4. Admin endpoint

- [ ] 4.1 Add `MapPost("/books/{id:int}/ingest-blocks", IngestBlocks).DisableAntiforgery()` to `BooksAdminEndpoints`.
- [ ] 4.2 Implement the `IngestBlocks` handler: `404` if record missing; `409` if `Processing`; otherwise `queue.TryEnqueue(new IngestionWorkItem(IngestionWorkType.IngestBlocks, id))` and return `Results.Accepted("/admin/books/{id}")`.
- [ ] 4.3 Add `BooksAdminEndpointsTests` cases: `IngestBlocks_RecordNotFound_Returns404`, `IngestBlocks_AlreadyProcessing_Returns409`, `IngestBlocks_Success_Returns202_AndEnqueues_IngestBlocksWork`.

## 5. Retrieval collection switch

- [ ] 5.1 Add `Retrieval:Collection` to `RetrievalOptions` (default `"chunks"`).
- [ ] 5.2 Update `RagRetrievalService` to read the configured collection name (mapping `"chunks"` → `QdrantOptions.CollectionName`, `"blocks"` → `QdrantOptions.BlocksCollectionName`, anything else → log warning and fall back to `chunks`) and pass it to the `IQdrantSearchClient` for every search.
- [ ] 5.3 Update `IQdrantSearchClient` and `QdrantSearchClientAdapter` to take a collection name per call (or per-instance via factory) so retrieval can target either collection without DI duplication.
- [ ] 5.4 Add tests in `RagRetrievalServiceTests` (or equivalent) covering: default routes to chunks, `Retrieval:Collection=blocks` routes to blocks, invalid value falls back to chunks with warning.

## 6. Documentation

- [ ] 6.1 Add to `DndMcpAICsharpFun.http`: an example block for `POST {{baseUrl}}/admin/books/1/ingest-blocks` with the admin key header and a one-line comment explaining it as the no-LLM path.
- [ ] 6.2 Add a comment near the retrieval examples explaining `Retrieval:Collection` and listing valid values (`chunks`, `blocks`).
- [ ] 6.3 Skim `README.md` (if present) for any TOC of admin endpoints; add the new one.

## 7. Verification

- [ ] 7.1 `dotnet build` — zero errors, no new warnings.
- [ ] 7.2 `dotnet test` — all tests pass, including the new ones.
- [ ] 7.3 Manual smoke test against the running PHB:
    1. Restart the stack so the new `dnd_blocks` collection is created.
    2. `DELETE /admin/books/{id}` for any prior block-test record.
    3. Register the PHB.
    4. `POST /admin/books/{id}/ingest-blocks` and confirm it completes in minutes (not hours).
    5. Use `GET /collections/dnd_blocks` against Qdrant to see point count.
    6. Set `Retrieval:Collection=blocks` and `/retrieval/search?q=fireball` — confirm results come back with block text and bookmark-derived `category=Spell`.
- [ ] 7.4 Compare retrieval quality side-by-side: same query against `chunks` and `blocks` with note on which feels more useful.
- [ ] 7.5 `openspec status --change block-direct-embedding` shows all artifacts done.
