## Context

`needsReview` is a bool on `EntityEnvelope`, set during extraction by `ExtractionNeedsReview.Derive` (low/medium confidence OR `HasOcrArtifacts(name)`), persisted to Qdrant (`EntityPayloadFields.NeedsReview`) and written into `books/canonical/<slug>.json`. The LLM `confidence` is deliberately not persisted. `CanonicalValidationService` only counts flagged entities per file. `EntityIngestionOrchestrator` already does the full pipeline (load → `EntityMerger` → `EntityCanonicalTextDispatcher.Render` → `IEmbeddingService.EmbedAsync` → `store.UpsertAsync` + `DeleteByFileHashExceptAsync`). `CanonicalJsonLoader` reads canonical files; there is no canonical *writer* yet (extraction writes via `JsonSerializer` directly).

## Goals / Non-Goals

**Goals:**
- List, inspect, and resolve flagged entities through admin endpoints.
- Canonical JSON stays the source of truth; Qdrant is kept consistent via targeted re-index.
- Show why each entity is flagged without new persistence.

**Non-Goals:**
- Blazor UI (later spec).
- Changing how `needsReview` is *set* during extraction (that's the normalizer/heuristic, unchanged here).
- Bulk content auto-fixing (resolve edits are explicit, per-entity or accept-only).

## Decisions

**1. Read model from canonical, not Qdrant.** The list/get read `books/canonical/*.json` (the source of truth) via `CanonicalJsonLoader`. This guarantees the queue matches what a resolve will edit, and avoids drift if Qdrant and canonical ever differ. Listing filters `e.NeedsReview == true`, optional `book` (slug) and `reason`, with `offset`/`limit` paging and a `total`.

**2. Reason derivation on read.** `reason = HasOcrArtifacts(name) ? "ocr-artifact" : "low-confidence"`. Pure function of the stored name; no schema change. (An entity flagged for both is reported as `ocr-artifact`, the actionable one.)

**3. Canonical writer.** Add a minimal `CanonicalJsonWriter` (or a write method on the loader) that loads a `<book>.json`, mutates one entity (clear `needsReview`; optionally set `name` / merge `fields`), and writes it back with the same serializer settings used by extraction (stable formatting, trailing newline). Edits target an entity by id.

**4. Targeted re-index.** Factor `ReindexEntityAsync(bookId, entityId)` out of `EntityIngestionOrchestrator`: load the single entity from canonical, run the same merge/render/embed, and `UpsertAsync` exactly that one `EntityPoint` (same FileHash). It must NOT call `DeleteByFileHashExceptAsync` (that's the whole-book cleanup); a single upsert overwrites the one point by its deterministic id. This keeps other points untouched.

**5. Resolve semantics.** `POST /admin/entities/{id}/resolve { action, name?, fields? }`:
- `accept`: clear `needsReview` only.
- `edit`: apply `name` and/or `fields` (shallow-merge provided field keys), then clear `needsReview`. An edited name MAY change the id slug; v1 keeps the id stable (edit name/fields but not id) to avoid orphan points — note this as a known limitation.
Both write canonical then `ReindexEntityAsync`. Idempotent: resolving an already-resolved entity is a no-op success.

**6. Bulk accept.** `POST /admin/entities/needs-review/accept { book?, reason? }` clears flags for the matching set in canonical and re-indexes each affected entity. Returns the count cleared.

**7. Endpoints + contracts.** New `NeedsReviewEndpoints` mapped under `/admin`. Update `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` in the same change (project rule).

## Risks / Trade-offs

- **Edit changing the natural id.** If a user renames an entity such that its slug would change, keeping the id stable means the id no longer matches the name. Accepted for v1 (documented); a future "rename with re-slug + orphan cleanup" can extend it.
- **Canonical write concurrency.** Two resolves on the same book file could race. v1 serializes writes per book file (simple lock) — acceptable for an admin tool.
- **Targeted reindex vs merge.** `ReindexEntityAsync` reuses the same merge path, so a single-entity reindex stays consistent with a full re-ingest. It must reuse, not duplicate, the per-entity logic to avoid divergence.
- **Listing cost.** Reading whole canonical files per list request is fine at this corpus size (4 files, ~2300 entities); revisit only if it grows large.
