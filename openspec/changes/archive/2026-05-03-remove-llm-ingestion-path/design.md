## Context

Two ingestion paths have been carried side-by-side: the original LLM-driven `extract → JSON → ingest-json` flow that produces typed entity points in `dnd_chunks`, and the newer no-LLM `ingest-blocks` flow that produces layout blocks in `dnd_blocks`. After validating both against a real PHB, the LLM path produces only ~959 sparse "chunks" (most empty or scrambled past usefulness on this hardware/model combo), while the block path produces ~3,156 useful blocks in minutes. For an MCP-as-RAG product where the consumer is itself an LLM, the block path is sufficient and the LLM path is dead weight.

This change removes the LLM path completely. The intent is not "deprecate" — it is "delete". Re-introduction later (e.g., to power a frontend that displays structured spell cards) will be designed against a clean foundation rather than carrying a known-broken parallel pipeline.

## Goals / Non-Goals

**Goals:**
- Eliminate every code path, configuration knob, dependency, route, work-item type, Qdrant collection, and test that exists solely to support the LLM ingestion flow.
- Leave the block-ingestion path and retrieval untouched in observable behaviour.
- Preserve `DELETE /admin/books/{id}` (registration cleanup) — that workflow is still needed for the block path.
- Preserve every domain type still consumed by the block path (`TocSectionEntry`, `BookmarkTocMapper`, `TocCategoryMap`, `BlockMetadata`, `BlockChunk`, `ChunkMetadata` for retrieval responses).
- Make the change atomic from an operational point of view: after one rebuild and restart, the system is in the new shape; no half-deployed state.

**Non-Goals:**
- Migrating existing data in `dnd_chunks`. Operators reclaim disk manually (`DELETE /collections/dnd_chunks` against Qdrant).
- Removing the `qwen2.5:7b` model from the local Ollama volume. Pull list is updated; existing volumes retain the model until pruned.
- Renaming `IngestionStatus.JsonIngested` to a more semantically appropriate `Ingested`. The enum value name lives in production SQLite as a string; renaming would require a data migration. The cosmetic improvement is not worth the complexity here.
- Renaming the `IngestionRecords` table or its columns. Same reasoning.
- Touching the `ChunkMetadata` retrieval response shape. Some of its fields (e.g. `chunkIndex`, `entityName`, `chapter`) made more sense for the LLM path; they remain in the response with sensible defaults so consumers do not break.

## Decisions

**1. Outright delete, not a feature flag.** Alternatives considered: a `Features:LlmExtractionEnabled` flag that disables the routes but keeps the code. Rejected because dead code accumulates implicit dependencies, never gets exercised, and rots. Re-introduction later is welcome but should start from a clean fork of the current state rather than a flagged-off implementation.

**2. Move `DeleteBookAsync` to a small dedicated `BookDeletionService` rather than keeping `IngestionOrchestrator` as a one-method shell.** Alternatives considered: leaving `IngestionOrchestrator` with just `DeleteBookAsync`, or merging deletion into `BlockIngestionOrchestrator`. Rejected the first because the class name no longer reflects its purpose; rejected the second because deletion is logically distinct from ingestion (it tears state down rather than building it up) and the merged class would have a cluttered surface.

**3. Drop `IExtractionCancellationRegistry` entirely.** It existed solely so a long-running LLM extract could be cancelled mid-book via `POST /cancel-extract`. Block ingestion is fast enough (minutes) that cooperative cancellation through the regular `IServiceScope` lifetime + `BackgroundService` shutdown is sufficient. If a user wants to abort a block run, they restart the app or live with the few minutes of work.

**4. Remove `IngestionStatus.Extracted` value.** It only ever held a record between `/extract` succeeding and `/ingest-json` running. With the path gone, no record can ever be in this state. Existing rows (if any) would already need data fixup; this change does not migrate them but a parallel SQLite column update is a one-liner if the deployment has any.

**5. The Ollama configuration shrinks but the option class stays.** `OllamaOptions` keeps `BaseUrl` and `EmbeddingModel` (both used by retrieval and block ingestion). Drop the four extraction-specific fields. Rename nothing.

**6. The `ollama-pull` entrypoint pulls only the embedding model.** Both compose files are updated. This means a fresh deployment downloads ~1 GB instead of ~5.5 GB. Existing deployments need to manually `ollama rm qwen2.5:7b` if they want disk back, but the API will simply never request that model again.

**7. `dnd_chunks` collection is left orphaned in existing Qdrant volumes.** No code-side cleanup runs; the initialiser simply stops creating it. We do not write a delete-on-startup hook because that would surprise an operator who wanted to keep the data around for one-off inspection. Documentation in the proposal points at the manual `curl DELETE` for those who want the disk back.

**8. Retrieval is hardcoded to the blocks collection.** The `Retrieval:Collection` runtime knob is removed. After this change the only meaningful knob is `Qdrant:BlocksCollectionName`, which most deployments leave at default. If a future change re-introduces a second collection, the knob comes back at that time — and the design will likely look different (per-query selection rather than startup config).

**9. Tests that targeted deleted classes are deleted, not stubbed.** No `[Fact(Skip = ...)]` or commented-out assertions. If a test cannot exist without a deleted class, the test goes too.

## Risks / Trade-offs

- **[Risk]** External tooling or scripts call the deleted endpoints and silently break. → **Mitigation:** the four endpoints all live in the same `BooksAdminEndpoints.cs`. After deletion they return 404. Document the breaking change in the proposal; the only known consumer is the project's own `.http` file, which is updated in the same change.
- **[Risk]** A future bug in the block path is harder to debug because we no longer have the LLM JSON-files-on-disk as ground truth. → **Mitigation:** the JSON files were always opaque; debugging used the page extractor anyway. Block ingestion logs every batch and Qdrant payload preserves source page + block order, which is enough.
- **[Trade-off]** `dnd_chunks` orphaned data in production Qdrant volumes. → Operators choose when to reclaim it. The collection cannot grow because nothing writes to it any more, so the cost is a one-time annoyance not an ongoing leak.
- **[Trade-off]** Loss of LLM-extracted typed fields (spell level, monster AC, etc.) that the block path does not produce. → Acceptable: the consumer is an LLM that recovers these from text on the fly. If a future product feature needs structured fields exposed, the LLM path can be re-introduced as an opt-in pipeline targeting a different collection.
- **[Risk]** `IngestionStatus.Extracted` removal breaks deserialisation of any persisted record currently in that status. → **Mitigation:** verify SQLite has zero rows with `Status = 'Extracted'` before the change is rolled out. The block path uses `Pending → Processing → JsonIngested → Failed`. A record that was in `Extracted` from the legacy flow becomes orphaned but is harmless if the user `DELETE`s and re-registers it; the new code never reads `Extracted` so it cannot crash on it.
- **[Risk]** The `IngestionStatus.JsonIngested` value is now a misnomer (no JSON is involved). → Acceptable cosmetic debt. Renaming would touch persisted data; the value stays for compatibility and is documented in code comments.

## Migration Plan

1. Apply the change on a feature branch.
2. Build & run tests; expect the test count to drop and pass.
3. Deploy: `docker compose up -d --build app`. The new container does not register the deleted routes and does not create `dnd_chunks`.
4. (Optional, operator-driven) Reclaim disk:
   - `curl -X DELETE http://qdrant:6333/collections/dnd_chunks` against Qdrant
   - `docker compose exec ollama ollama rm qwen2.5:7b`
5. Verify `/ingest-blocks` and `/retrieval/search` continue to work end-to-end (smoke test on a registered book).

Rollback: revert the merge commit. The four deleted endpoints, the second collection, and the LLM model pull all come back. `dnd_chunks` will be re-created empty on the next startup. No data is destroyed by this change, so rollback is non-destructive.
