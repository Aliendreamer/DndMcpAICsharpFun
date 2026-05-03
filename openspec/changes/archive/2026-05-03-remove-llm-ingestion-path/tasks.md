# Implementation Tasks

## 1. Carve out DeleteBookAsync into its own service

- [ ] 1.1 Create `Features/Ingestion/IBookDeletionService.cs` with `Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken ct = default)`.
- [ ] 1.2 Create `Features/Ingestion/BookDeletionService.cs` whose implementation matches the current `IngestionOrchestrator.DeleteBookAsync` body verbatim (deletes Qdrant points by hash if applicable, deletes the file, deletes the SQLite row). It depends on `IIngestionTracker`, `IVectorStoreService`, `ILogger<BookDeletionService>`. Note: with `dnd_chunks` removed, the vector-store delete now targets `dnd_blocks` via `DeleteBlocksByHashAsync`.
- [ ] 1.3 Register `IBookDeletionService → BookDeletionService` (Scoped) in `ServiceCollectionExtensions.AddIngestionPipeline`.
- [ ] 1.4 Update `BooksAdminEndpoints.DeleteBook` to inject `IBookDeletionService` instead of `IIngestionOrchestrator`.

## 2. Delete the LLM ingestion path

- [ ] 2.1 Delete `Features/Ingestion/Extraction/ILlmEntityExtractor.cs`.
- [ ] 2.2 Delete `Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs`.
- [ ] 2.3 Delete `Features/Ingestion/Extraction/IJsonIngestionPipeline.cs`.
- [ ] 2.4 Delete `Features/Ingestion/Extraction/JsonIngestionPipeline.cs`.
- [ ] 2.5 Delete `Features/Ingestion/Extraction/IEntityJsonStore.cs`.
- [ ] 2.6 Delete `Features/Ingestion/Extraction/EntityJsonStore.cs`.
- [ ] 2.7 Delete `Features/Ingestion/Extraction/PageBlockGrouper.cs`.
- [ ] 2.8 Delete `Domain/ExtractedEntity.cs`.
- [ ] 2.9 Delete `Domain/ContentChunk.cs` if it has no remaining consumers (check; `IEmbeddingIngestor` may use it — if so, refactor that too).
- [ ] 2.10 Delete `Domain/PageData.cs` if its only consumer was `EntityJsonStore` / `ExtractSinglePageAsync`.
- [ ] 2.11 Delete `Features/Ingestion/IExtractionCancellationRegistry.cs` and `ExtractionCancellationRegistry.cs`.
- [ ] 2.12 Delete `Features/Ingestion/IngestionOrchestrator.cs` and `IIngestionOrchestrator.cs`. (Method `DeleteBookAsync` was already moved in Task 1.)

## 3. Trim queue, work types, and worker

- [ ] 3.1 In `Features/Ingestion/IIngestionQueue.cs`, change `IngestionWorkType` to a single-value enum: `enum IngestionWorkType { IngestBlocks }`.
- [ ] 3.2 In `Features/Ingestion/IngestionQueueWorker.cs`: remove the `Extract` and `IngestJson` branches, remove the `IIngestionOrchestrator` dependency, remove the `IExtractionCancellationRegistry` dependency. The remaining body: dequeue → `IBlockIngestionOrchestrator.IngestBlocksAsync(...)`.
- [ ] 3.3 Remove the cancellation-registry registration from `ServiceCollectionExtensions`.

## 4. Remove the LLM admin endpoints

- [ ] 4.1 In `Features/Admin/BooksAdminEndpoints.cs`: delete the route registrations and handler methods for `ExtractBook`, `IngestJson`, `ExtractPage`, `CancelExtract`. Keep `RegisterBook`, `GetAllBooks`, `IngestBlocks`, `DeleteBook`, and `GetExtracted` (the last just lists files; if its only purpose was inspecting LLM JSON output it can also go — see check below).
- [ ] 4.2 Decide on `GetExtracted` (`GET /admin/books/{id}/extracted`): with `EntityJsonStore` deleted, its `IEntityJsonStore.ListPageFiles` dependency disappears. Delete the route and its handler too.
- [ ] 4.3 Delete `IngestionRecord` lookups that branched on `IngestionStatus.Extracted` (search the codebase for that enum value's call sites).

## 5. Trim configuration and DB enum

- [ ] 5.1 In `Infrastructure/Sqlite/IngestionStatus.cs`, remove the `Extracted` enum value.
- [ ] 5.2 In `Infrastructure/Qdrant/QdrantOptions.cs`, remove `CollectionName`. Keep `BlocksCollectionName`.
- [ ] 5.3 In `Infrastructure/Qdrant/QdrantCollectionInitializer.cs`, remove the call that ensures `dnd_chunks`. Only `dnd_blocks` is ensured.
- [ ] 5.4 In `Infrastructure/Ollama/OllamaOptions.cs`, remove `ExtractionModel`, `ExtractionNumCtx`, `ExtractionTimeoutSeconds`. Keep `BaseUrl`, `EmbeddingModel`.
- [ ] 5.5 In `Features/Ingestion/IngestionOptions.cs` (or wherever the option lives), remove `LlmExtractionRetries`. Keep `BooksPath`, `DatabasePath`, `MinPageCharacters`, `MaxChunkTokens`, `OverlapTokens`, `EmbeddingBatchSize`.
- [ ] 5.6 In `Features/Retrieval/RetrievalOptions.cs`, remove the `Collection` field. Keep `ScoreThreshold` and `MaxTopK`.
- [ ] 5.7 In `Features/Retrieval/RagRetrievalService.cs`, remove the `ResolveCollection` helper, the `LogUnknownCollectionMode` partial method, and the `ILogger` constructor parameter that was only added for that warning. Hardcode the service to `qdrantOptions.Value.BlocksCollectionName`.
- [ ] 5.8 In `Features/Retrieval/QdrantPayloadMapper.cs`, drop fields no longer present on block points (e.g., the `entity_name` and `chapter` reads will return null/empty for blocks; that is fine because `ChunkMetadata` still has those properties as nullable / default — leave the helper as-is unless cleanup is trivial).
- [ ] 5.9 Update `Config/appsettings.json`: remove the keys listed in 5.2, 5.4, 5.5, 5.6. Same for `Config/appsettings.Development.json` if it overrides any.

## 6. Compose & ops

- [ ] 6.1 In `docker-compose.yml`, change the `ollama-pull` entrypoint from `"ollama pull mxbai-embed-large && ollama pull qwen2.5:7b"` to `"ollama pull mxbai-embed-large"`.
- [ ] 6.2 Same change in `docker-compose.prod.yml`.
- [ ] 6.3 No code-side cleanup of the existing `dnd_chunks` collection or the existing `qwen2.5:7b` model in the volume — operators do this manually if they want disk back. Document the commands in the proposal (already done).

## 7. .http file

- [ ] 7.1 Delete the `### Admin Books — Extract book with LLM ...` block.
- [ ] 7.2 Delete the `### Admin Books — List extracted JSON files for a book` block (only relevant when `/extracted` route still existed).
- [ ] 7.3 Delete the `### Admin Books — Ingest from extracted JSON ...` block.
- [ ] 7.4 Delete the `### Admin Books — Cancel an in-progress extraction ...` block.
- [ ] 7.5 Delete the `### Extract single page ...` blocks (both with and without `?save=true`).
- [ ] 7.6 Delete the `# Retrieval:Collection ...` comment near the retrieval section.

## 8. Tests

- [ ] 8.1 Delete `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaLlmEntityExtractorTests.cs` if it still exists.
- [ ] 8.2 Delete tests targeting `JsonIngestionPipeline`, `EntityJsonStore`, `PageBlockGrouper` (search the test project for these class names).
- [ ] 8.3 Delete tests in `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs` for the removed routes (`ExtractBook_*`, `IngestJson_*`, `ExtractPage_*`, `CancelExtract_*`, `GetExtracted_*`).
- [ ] 8.4 Update `DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs`: rename to `BookDeletionServiceTests.cs` and trim to only cover `DeleteBookAsync` scenarios (NotFound / Conflict / Deleted with vectors / Deleted without vectors).
- [ ] 8.5 Update `DndMcpAICsharpFun.Tests/Retrieval/RagRetrievalServiceTests.cs` BuildSut helper to remove the `ILogger` parameter that was added for the now-removed collection-resolution logic.
- [ ] 8.6 Search for `OllamaLlmExtractionService`, `IExtractionCancellationRegistry`, `IIngestionOrchestrator`, `Retrieval:Collection`, `IngestionWorkType.Extract`, `IngestionWorkType.IngestJson`, `IngestionStatus.Extracted` across the test project and remove every usage.

## 9. Verification

- [ ] 9.1 `dotnet build` — zero errors and no new warnings.
- [ ] 9.2 `dotnet test` — all surviving tests pass; expected count somewhere in the 130-150 range (down from 177).
- [ ] 9.3 `docker compose up -d --build app` against a clean local stack: confirm in logs that `Created Qdrant collection 'dnd_blocks'` appears and `dnd_chunks` is not mentioned.
- [ ] 9.4 `curl -s http://localhost:6333/collections | jq '.result.collections[].name'` after the change in a fresh deployment shows `dnd_blocks` only (or `dnd_blocks` plus any pre-existing orphaned `dnd_chunks` from before the change).
- [ ] 9.5 Smoke test: register a PDF → `POST /ingest-blocks` → `GET /retrieval/search?q=fireball` returns useful hits. End-to-end works without ever touching the deleted endpoints.
- [ ] 9.6 `openspec status --change remove-llm-ingestion-path` shows all four artifacts done.
