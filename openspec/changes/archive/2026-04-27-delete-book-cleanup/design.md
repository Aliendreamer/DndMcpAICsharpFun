## Context

Book records are registered in SQLite, their PDFs stored on disk, and their content embedded as vectors in Qdrant with deterministic point IDs (`SHA256(fileHash + chunkIndex)[0..16]` as a `Guid`). There is currently no delete path. Administrators have no way to remove bad registrations without manually editing the database and Qdrant collection.

The `IngestBookAsync` method currently computes the hash mid-flow, after an early-exit check. This makes it impossible to do hash-based duplicate detection before ingestion begins. The file hash is also not persisted until after ingestion completes, so newly registered records have an empty hash.

## Goals / Non-Goals

**Goals:**
- Provide `DELETE /admin/books/{id}` that atomically cleans up all three storage layers
- Block deletion of in-progress records (409) to avoid partial cleanup races
- Allow deletion of Pending, Failed, and Duplicate records without touching Qdrant
- Add hash-first flow to `IngestBookAsync` enabling duplicate detection and single hash computation
- Keep all orchestration in `IIngestionOrchestrator` — endpoint stays thin

**Non-Goals:**
- Bulk delete
- Soft delete / recycle bin
- Cascading re-ingestion of related records on delete

## Decisions

### D1: Return value over exceptions for delete results
`DeleteBookAsync` returns `DeleteBookResult` (`Deleted`, `NotFound`, `Conflict`) rather than throwing. The endpoint maps the enum to HTTP status codes. Exceptions are reserved for genuine infrastructure failures. This keeps the control flow explicit and avoids using exceptions for expected outcomes.

### D2: Qdrant deletion reconstructs point IDs from hash + chunk count
Point IDs are deterministic: `DerivePointId(fileHash, i)` for `i` in `0..chunkCount-1`. For Completed records both values are stored in the SQLite row — no separate index or payload query is needed. `IVectorStoreService` gets `DeleteByHashAsync(fileHash, chunkCount, ct)` which builds the ID list and calls `QdrantClient.DeleteAsync` in one batch.

Alternative considered: filter delete by payload field (`source_book` or `file_hash`). Rejected because payload-based deletes require a scroll+delete pattern or a filter delete that Qdrant only supports on indexed fields, whereas the deterministic ID approach is a single O(n) batch with no index dependency.

### D3: Hash computed first in IngestBookAsync, stored immediately
`IngestBookAsync` now: (1) computes hash, (2) persists it via `MarkProcessingAsync` (extended to also store the hash), (3) checks for existing Completed record with same hash → Duplicate, (4) checks if current record is already Completed with same hash → skip unchanged, (5) proceeds with chunking and embedding. This single hash computation replaces the current mid-flow compute and the post-completion store.

### D4: Delete goes through IIngestionOrchestrator, not a new service
Deletion touches the same infrastructure the orchestrator already owns (tracker, vector store, disk). Adding it to the orchestrator keeps the surface area minimal and avoids a thin service that would just delegate. Option B (dedicated `IBookDeletionService`) was considered but rejected as premature — the orchestrator remains cohesive as the book-lifecycle manager.

## Risks / Trade-offs

- **Race: Pending→Processing during delete** — A record checked as Pending could transition to Processing between the status read and the delete. Mitigation: `DeleteAsync` in the tracker uses a conditional delete (`WHERE status != Processing`) and returns a boolean indicating whether a row was deleted. If false and the record exists, the orchestrator returns `Conflict`.
- **Partial Qdrant cleanup on failure** — If disk delete succeeds but Qdrant delete fails (or vice versa), the record is gone from SQLite but artefacts remain. Mitigation: delete SQLite row last; a failed Qdrant or disk step leaves the record intact so the operator can retry.
- **Large chunkCount batch to Qdrant** — A 200 MB book may produce thousands of chunks. Deleting all point IDs in one batch is acceptable for admin-frequency operations but could be chunked if Qdrant imposes a per-request limit. Deferred until observed in practice.

## Migration Plan

No schema migration required — `IngestionStatus.Duplicate` is already present. The hash column on `IngestionRecord` already exists. Deploy is a standard rolling restart.
