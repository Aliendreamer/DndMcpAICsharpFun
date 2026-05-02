## Why

The LLM-driven extract → JSON → ingest path has been validated against a real PHB and found to be high-cost (hours of inference per book) and low-yield (only spell entities consistently extracted; monsters, classes, rules, conditions came back empty or scrambled). The block-direct-embedding path, in contrast, ingests the same book in minutes, produces ~3,000 useful chunks vs ~959 LLM "chunks" of which most were near-empty, and gives comparable retrieval quality for the MCP-as-RAG use case the project is targeting.

We are now committing to the block path as the only ingestion path. Carrying the LLM path as dead weight slows iteration on the part that actually works. If the LLM-extracted-entities approach becomes useful again (e.g. when adding a frontend that displays structured spell/monster cards), it can be reintroduced as a separate, opt-in feature; the current block-path infrastructure is unaffected by deletion of the legacy path.

## What Changes

- **BREAKING — admin endpoints deleted:**
  - `POST /admin/books/{id}/extract` (LLM extraction → JSON files)
  - `POST /admin/books/{id}/ingest-json` (JSON → embed → upsert to `dnd_chunks`)
  - `POST /admin/books/{id}/extract-page/{pageNumber}` (single-page LLM extraction)
  - `POST /admin/books/{id}/cancel-extract` (cancellation registry was only used by the LLM extract worker)
- **BREAKING — work item types removed:** `IngestionWorkType.Extract`, `IngestionWorkType.IngestJson`. Only `IngestionWorkType.IngestBlocks` remains.
- **BREAKING — Qdrant collection `dnd_chunks` no longer created.** `QdrantCollectionInitializer` only ensures `dnd_blocks`. The `Qdrant:CollectionName` config key is removed; `Qdrant:BlocksCollectionName` is the only collection name.
- **BREAKING — `Retrieval:Collection` config flag removed.** Retrieval always queries the blocks collection. Any callers passing the flag are silently ignored after this change.
- **BREAKING — Ollama config trimmed.** `Ollama:ExtractionModel`, `Ollama:ExtractionNumCtx`, `Ollama:ExtractionTimeoutSeconds` and the `LlmExtractionRetries` ingestion option are removed. The `qwen2.5:7b` model is removed from the `ollama-pull` entrypoint in both compose files.
- **Code deletions (whole files):**
  - `Features/Ingestion/Extraction/ILlmEntityExtractor.cs`, `OllamaLlmEntityExtractor.cs`
  - `Features/Ingestion/Extraction/IJsonIngestionPipeline.cs`, `JsonIngestionPipeline.cs`
  - `Features/Ingestion/Extraction/IEntityJsonStore.cs`, `EntityJsonStore.cs`, `PageData.cs` (if unused after deletion)
  - `Features/Ingestion/Extraction/PageBlockGrouper.cs`
  - `Features/Ingestion/IExtractionCancellationRegistry.cs`, `ExtractionCancellationRegistry.cs`
  - `Domain/ExtractedEntity.cs`, `Domain/ContentChunk.cs`
  - `Features/Ingestion/IngestionOrchestrator.cs` and `IIngestionOrchestrator.cs` (the only surviving method, `DeleteBookAsync`, moves to a new `IBookDeletionService` / `BookDeletionService` pair)
  - All tests that target deleted classes
- **Code modifications:**
  - `Features/Admin/BooksAdminEndpoints.cs`: drop the four deleted routes and their handlers; rename DI-injected dependency from `IIngestionOrchestrator` to `IBookDeletionService` for `DELETE /admin/books/{id}`.
  - `Features/Ingestion/IIngestionQueue.cs`: enum becomes `enum IngestionWorkType { IngestBlocks }`.
  - `Features/Ingestion/IngestionQueueWorker.cs`: dispatches only `IngestBlocks`; cancellation-registry plumbing removed.
  - `Infrastructure/Sqlite/IngestionStatus.cs`: drop the `Extracted` value (only the LLM path used it).
  - `Infrastructure/Qdrant/QdrantCollectionInitializer.cs`: remove the `dnd_chunks` ensure-call; only ensure `dnd_blocks`.
  - `Infrastructure/Qdrant/QdrantOptions.cs`: remove `CollectionName`. Keep `BlocksCollectionName`.
  - `Features/Retrieval/RetrievalOptions.cs`: remove `Collection` field.
  - `Features/Retrieval/RagRetrievalService.cs`: remove the collection-resolution helper; the service always points at `BlocksCollectionName`.
  - `Features/Retrieval/QdrantPayloadMapper.cs`: trim back to fields actually present on block points.
- **Config / infra:**
  - `Config/appsettings.json`: drop the keys listed above.
  - `docker-compose.yml` and `docker-compose.prod.yml`: simplify the `ollama-pull` entrypoint to pull only `mxbai-embed-large`.
  - `DndMcpAICsharpFun.http`: remove example blocks for the four deleted routes; remove the `Retrieval:Collection` comment.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `ingestion-pipeline`: extraction is now single-stage (`/ingest-blocks`); the multi-stage extract → JSON → ingest flow is removed.
- `llm-extraction`: this capability disappears entirely as a current behaviour. The spec entry is preserved for historical reference but every active requirement is removed (delta uses `## REMOVED Requirements`).
- `embedding-vector-store`: only one collection (`dnd_blocks`) is created and managed.
- `rag-retrieval`: removes the collection-selection requirement; queries always run against `dnd_blocks`.
- `http-contracts`: removes the four LLM-path admin endpoints from the `.http` file's coverage requirement.
- `block-ingestion`: unchanged in behaviour, but is now the *only* ingestion path; this is a documentation update only, no requirement change.

## Impact

- **Code:** ~1,500 lines deleted across 12 files. Roughly 400 net lines deleted from modified files (smaller orchestrator surface, smaller queue worker, smaller mapper). One small new file: `BookDeletionService` (~40 lines, owning the previously-orphaned `DeleteBookAsync` from `IngestionOrchestrator`).
- **API:** 4 admin endpoints removed (breaking). Consumers (the `.http` file, any external scripts) must stop calling them. No replacement is provided — `/ingest-blocks` is the path forward.
- **Configuration:** 7 keys removed across `appsettings.json` and the two compose files. Operators with custom values for any of the removed keys lose them silently; the migration guide is "your existing values were ignored before this change either way unless you were running the LLM path, which is no longer supported."
- **Storage:** `dnd_chunks` collection in Qdrant becomes orphaned (not deleted by code). Operators can drop it manually with `curl -X DELETE http://qdrant:6333/collections/dnd_chunks` to reclaim disk.
- **Disk:** removing `qwen2.5:7b` from the pull list saves ~4.5 GB on a fresh `ollama_data` volume. Existing volumes still contain the model until manually pruned (`ollama rm qwen2.5:7b`).
- **Tests:** ~30-40 test cases deleted (everything targeting `OllamaLlmEntityExtractor`, `JsonIngestionPipeline`, `EntityJsonStore`, the orchestrator's `ExtractBookAsync` / `ExtractSinglePageAsync` / `IngestJsonAsync`, the four admin endpoints, `IExtractionCancellationRegistry`). Net test count drops from 177 to roughly 130-140; remaining tests cover the block path, retrieval, registration, deletion, and Qdrant payload mapping.
- **Operational:** simpler mental model. One ingestion path, one collection, one model in active use during ingestion, no per-book staging on disk, no JSON intermediate. Future re-introduction (e.g., for a frontend that wants structured spell cards) starts from a clean foundation rather than a broken-but-still-running parallel pipeline.
