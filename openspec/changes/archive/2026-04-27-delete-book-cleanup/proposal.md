## Why

Book records can be registered with incorrect metadata (wrong version, placeholder display name) and there is currently no way to remove them. Administrators need a delete endpoint that performs a full cleanup — removing the SQLite record, the file from disk, and all associated vectors from Qdrant.

## What Changes

- Add `DELETE /admin/books/{id}` endpoint returning 204, 404, or 409
- Extend `IIngestionOrchestrator` with `DeleteBookAsync(int id, CancellationToken ct)` returning a `DeleteBookResult` enum (`Deleted`, `NotFound`, `Conflict`)
- Add `IVectorStoreService.DeleteByHashAsync(string fileHash, int chunkCount, CancellationToken ct)` to remove Qdrant points by reconstructing deterministic point IDs
- Fix `IngestBookAsync` to compute the file hash once at the top, reusing it for duplicate detection, unchanged-file skip, and storing on the record
- Add duplicate detection in `IngestBookAsync`: if another `Completed` record with the same hash exists, mark the new record as `Duplicate` and stop

## Capabilities

### New Capabilities
- `book-deletion`: Admin endpoint and orchestrator logic for fully removing a book record, its file, and its Qdrant vectors

### Modified Capabilities
- `ingestion-pipeline`: Hash is now computed once and used for both duplicate detection and the unchanged-file guard; `Duplicate` status is now reachable via normal ingestion flow

## Impact

- `Features/Admin/BooksAdminEndpoints.cs` — new route
- `Features/Ingestion/IIngestionOrchestrator.cs` — new method
- `Features/Ingestion/IngestionOrchestrator.cs` — new method + hash-first refactor
- `Features/VectorStore/IVectorStoreService.cs` — new delete method
- `Features/VectorStore/QdrantVectorStoreService.cs` — implements delete
- `Infrastructure/Sqlite/IIngestionTracker.cs` + `SqliteIngestionTracker.cs` — new `DeleteAsync` method
- `DELETE /admin/books/{id}` added to `DndMcpAICsharpFun.http`
