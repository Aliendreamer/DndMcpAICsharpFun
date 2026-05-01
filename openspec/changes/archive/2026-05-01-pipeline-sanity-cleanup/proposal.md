## Why

The codebase accumulated two ingestion pipelines during development: an old pattern-based chunking pipeline (PDF → `DndChunker` → embed) and the current LLM extraction pipeline (PDF → `ExtractBookAsync` → JSON → `IngestJsonAsync` → embed). Only the new pipeline is used going forward. Additionally, calling `/extract` on an already-ingested book leaves stale Qdrant vectors behind because no cleanup happens before re-extraction starts.

## What Changes

- Delete the entire `Features/Ingestion/Chunking/` folder and all pattern detector classes
- Delete `NoOpEmbeddingIngestor` (never registered, never used)
- Delete `IIngestionOrchestrator.IngestBookAsync` and its implementation
- Remove `POST /admin/books/{id}/reingest` endpoint and its `.http` example
- Remove `IngestionWorkType.Reingest` and its handling in `IngestionQueueWorker`
- Remove `IngestionStatus.Completed` (only ever set by the deleted `IngestBookAsync`)
- Remove all 9 chunking service registrations from `ServiceCollectionExtensions`
- Delete all chunking-related tests
- Fix `ExtractBookAsync` to clean up before re-extracting: delete Qdrant vectors (if `JsonIngested`), delete JSON files, reset status — so `/extract` is safe to call at any point in the book lifecycle

## Capabilities

### New Capabilities
- none

### Modified Capabilities
- `ingestion-pipeline`: `IngestBookAsync` / Reingest path removed; `ExtractBookAsync` gains idempotent re-extraction cleanup

## Impact

- `Features/Ingestion/Chunking/` — deleted entirely
- `Features/Embedding/NoOpEmbeddingIngestor.cs` — deleted
- `Features/Ingestion/IngestionOrchestrator.cs` — `IngestBookAsync` removed, `ExtractBookAsync` gains cleanup logic
- `Features/Ingestion/IIngestionOrchestrator.cs` — `IngestBookAsync` removed
- `Features/Admin/BooksAdminEndpoints.cs` — `/reingest` endpoint removed
- `Features/Ingestion/IngestionQueueWorker.cs` — `Reingest` case removed
- `Infrastructure/Sqlite/IngestionOptions.cs` / status enum — `Completed` value removed
- `Extensions/ServiceCollectionExtensions.cs` — 9 chunking registrations removed
- `DndMcpAICsharpFun.http` — reingest example removed
- `DndMcpAICsharpFun.Tests/Ingestion/Chunking/` — deleted entirely
