## Context

Two ingestion pipelines were built in sequence. The old pipeline (`IngestBookAsync`) used `DndChunker` with 7 regex-based `IPatternDetector` implementations to split PDF text into entity blocks, then embedded them directly via `EmbeddingIngestor`. The new pipeline (`ExtractBookAsync` + `IngestJsonAsync`) uses an LLM to extract structured JSON entities per page, persists them to disk, then embeds from JSON. The old pipeline is still wired up and reachable via `/reingest`, but is not the recommended flow and produces lower-quality embeddings.

Additionally, `ExtractBookAsync` has no idempotency guard: calling it on a `JsonIngested` book silently overwrites JSON files but leaves the existing Qdrant vectors untouched, causing duplicates on the next `ingest-json` call.

## Goals / Non-Goals

**Goals:**
- Remove all code, registrations, and tests belonging to the old chunking pipeline
- Make `ExtractBookAsync` safe to call repeatedly at any book status
- Leave `ContentChunk`, `ChunkMetadata`, `ContentCategory` intact — still used by `JsonIngestionPipeline`

**Non-Goals:**
- Changing the LLM extraction logic itself
- Changing the embedding or vector store services
- Adding new API surface

## Decisions

**Delete `IngestionStatus.Completed` along with `IngestBookAsync`**
`Completed` is only ever set by `MarkCompletedAsync`, which is only called from `IngestBookAsync`. Once that method is deleted, `Completed` becomes an orphaned enum value with no production code path that sets it. Keeping it would mislead readers into thinking it's reachable. Remove it.

**Idempotency cleanup lives inside `ExtractBookAsync`, not the endpoint**
The endpoint just enqueues work and returns 202. Business logic (cleanup, state transitions) belongs in the orchestrator. `ExtractBookAsync` checks `record.Status` at the start and performs the appropriate cleanup before extraction begins:
- `JsonIngested` → `vectorStore.DeleteByHashAsync(fileHash, chunkCount)` + `jsonStore.DeleteAllPages` + `tracker.ResetForReingestionAsync`
- `Extracted` → `jsonStore.DeleteAllPages` + `tracker.ResetForReingestionAsync`
- `Pending` / `Failed` → no cleanup needed, proceed directly

**`ResetForReingestionAsync` is the right reset primitive**
It already clears `Status → Pending`, `ChunkCount → null`, `IngestedAt → null`, `Error → null`. Reusing it avoids duplicating reset logic.

## Risks / Trade-offs

- **`IngestionStatus.Completed` may exist in the SQLite database** for books previously ingested via the old pipeline. After deletion from the enum, EF Core will return an unknown integer value for those rows. Mitigation: add a migration or a note that those records should be re-extracted. Since this is local dev-only infrastructure, a manual `DELETE FROM IngestionRecords WHERE Status = <Completed-value>` is acceptable.
- **Deleting chunking tests reduces test count** — expected and intentional. The new tests added for idempotency partially offset this.
