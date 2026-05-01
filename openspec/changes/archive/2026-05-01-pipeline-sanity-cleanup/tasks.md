## 1. Delete Chunking Pipeline Files

- [x] 1.1 Delete `Features/Ingestion/Chunking/` directory and all its contents (`DndChunker.cs`, `ContentCategoryDetector.cs`, `ChapterContextTracker.cs`, `EntityNameExtractor.cs`, `IPatternDetector.cs`, all 7 detector files)
- [x] 1.2 Delete `Features/Embedding/NoOpEmbeddingIngestor.cs`

## 2. Remove IngestBookAsync from Orchestrator

- [x] 2.1 Remove `IngestBookAsync` method signature from `Features/Ingestion/IIngestionOrchestrator.cs`
- [x] 2.2 Delete the `IngestBookAsync` method body from `Features/Ingestion/IngestionOrchestrator.cs` including all its `[LoggerMessage]` entries that are only used by that method

## 3. Remove Reingest Endpoint and Queue Work Type

- [x] 3.1 Remove `POST /admin/books/{id}/reingest` handler from `Features/Admin/BooksAdminEndpoints.cs` and its `MapPost` registration
- [x] 3.2 Remove `IngestionWorkType.Reingest` from the enum and its `case Reingest:` handler in `Features/Ingestion/IngestionQueueWorker.cs`
- [x] 3.3 Remove the reingest example request from `DndMcpAICsharpFun.http`

## 4. Remove IngestionStatus.Completed

- [x] 4.1 Remove `Completed` from the `IngestionStatus` enum (in `Infrastructure/Sqlite/` or `Domain/`)
- [x] 4.2 Remove `MarkCompletedAsync` from `IIngestionTracker` and `SqliteIngestionTracker`
- [x] 4.3 Remove any remaining references to `IngestionStatus.Completed` (e.g. in `DeleteBookAsync` status check in the orchestrator)

## 5. Remove Chunking Service Registrations

- [x] 5.1 Remove all 9 chunking service registrations from `Extensions/ServiceCollectionExtensions.cs`: `SpellPatternDetector`, `MonsterPatternDetector`, `ClassPatternDetector`, `BackgroundPatternDetector`, `TreasurePatternDetector`, `EncounterPatternDetector`, `TrapPatternDetector`, `ContentCategoryDetector`, `DndChunker`

## 6. Fix ExtractBookAsync Idempotency

- [x] 6.1 At the start of `ExtractBookAsync` (after loading the record), add a cleanup block:
  - If `record.Status == JsonIngested`: call `await vectorStore.DeleteByHashAsync(record.FileHash, record.ChunkCount!.Value, CancellationToken.None)`, then `jsonStore.DeleteAllPages(recordId)`, then `await tracker.ResetForReingestionAsync(recordId, CancellationToken.None)`
  - If `record.Status == Extracted`: call `jsonStore.DeleteAllPages(recordId)`, then `await tracker.ResetForReingestionAsync(recordId, CancellationToken.None)`
  - Otherwise: no cleanup needed

## 7. Delete Chunking Tests

- [x] 7.1 Delete `DndMcpAICsharpFun.Tests/Ingestion/Chunking/` directory and all test files within it
- [x] 7.2 Remove any test files testing `IngestBookAsync` or the reingest endpoint specifically (check `IngestionOrchestratorTests.cs` for tests that only cover `IngestBookAsync`)

## 8. Add Idempotency Tests

- [x] 8.1 Add to `IngestionOrchestratorTests.cs`: `ExtractBookAsync_WhenJsonIngested_DeletesVectorsAndJsonThenResets` — mock record with `JsonIngested` status + chunkCount, verify `DeleteByHashAsync`, `DeleteAllPages`, and `ResetForReingestionAsync` are called before extraction proceeds
- [x] 8.2 Add: `ExtractBookAsync_WhenExtracted_DeletesJsonAndResets` — mock record with `Extracted` status, verify `DeleteAllPages` and `ResetForReingestionAsync` called, `DeleteByHashAsync` NOT called
- [x] 8.3 Add: `ExtractBookAsync_WhenPending_NoCleanup` — mock record with `Pending` status, verify none of the cleanup methods are called

## 9. Build and Verify

- [x] 9.1 Run `dotnet build` — must succeed with 0 errors
- [x] 9.2 Run `dotnet test` — all remaining tests must pass
- [x] 9.3 Commit: `refactor: remove legacy chunking pipeline, fix extract idempotency`
