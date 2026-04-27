## 1. Ingestion tracker — delete support

- [ ] 1.1 Add `DeleteAsync(int id, CancellationToken ct)` to `IIngestionTracker` returning `bool` (true = deleted, false = not found or Processing)
- [ ] 1.2 Implement `DeleteAsync` in `SqliteIngestionTracker` using a conditional `ExecuteDeleteAsync` where `status != Processing`; return false if no row was deleted
- [ ] 1.3 Add `MarkHashAsync(int id, string fileHash, CancellationToken ct)` to `IIngestionTracker` and implement in `SqliteIngestionTracker` (persists hash when Processing begins)

## 2. Ingestion tracker — duplicate detection

- [ ] 2.1 Add `GetCompletedByHashAsync(string hash, CancellationToken ct)` to `IIngestionTracker` returning `IngestionRecord?`
- [ ] 2.2 Implement `GetCompletedByHashAsync` in `SqliteIngestionTracker`

## 3. Vector store — delete support

- [ ] 3.1 Add `DeleteByHashAsync(string fileHash, int chunkCount, CancellationToken ct)` to `IVectorStoreService`
- [ ] 3.2 Implement in `QdrantVectorStoreService`: reconstruct point IDs for indices `0..chunkCount-1` using existing `DerivePointId`, call `QdrantClient.DeleteAsync` with the ID list in one batch

## 4. Orchestrator — hash-first refactor

- [ ] 4.1 Refactor `IngestBookAsync` to compute file hash first and call `MarkHashAsync` before any other work
- [ ] 4.2 Add duplicate detection: call `GetCompletedByHashAsync`; if a different completed record exists, call `tracker.MarkDuplicateAsync` (new tracker method) and return
- [ ] 4.3 Add `MarkDuplicateAsync(int id, CancellationToken ct)` to `IIngestionTracker` and implement in `SqliteIngestionTracker`
- [ ] 4.4 Reuse the already-computed hash for the unchanged-file guard (remove second hash computation)

## 5. Orchestrator — delete method

- [ ] 5.1 Add `DeleteBookAsync(int id, CancellationToken ct)` returning `DeleteBookResult` to `IIngestionOrchestrator`
- [ ] 5.2 Add `DeleteBookResult` enum (`Deleted`, `NotFound`, `Conflict`) in the Ingestion feature folder
- [ ] 5.3 Implement `DeleteBookAsync` in `IngestionOrchestrator`: load record → if null return `NotFound` → if `Processing` return `Conflict` → if `Completed` delete Qdrant vectors then file then SQLite row → else delete file then SQLite row → return `Deleted`

## 6. Admin endpoint

- [ ] 6.1 Register `DELETE /admin/books/{id:int}` in `MapBooksAdmin`
- [ ] 6.2 Implement `DeleteBook` handler: call `orchestrator.DeleteBookAsync`, map `Deleted`→204, `NotFound`→404, `Conflict`→409 with message

## 7. HTTP contracts

- [ ] 7.1 Add `DELETE /admin/books/{id}` example to `DndMcpAICsharpFun.http`

## 8. Spec update

- [ ] 8.1 Archive the `ingestion-pipeline` delta spec into `openspec/specs/ingestion-pipeline/spec.md`
- [ ] 8.2 Create `openspec/specs/book-deletion/spec.md` from the new capability spec
