## Why

About 254 entities across the four books carry `needsReview = true` — flagged during extraction for an OCR-artifact name or low LLM confidence. Today this is only surfaced as per-file *counts* in the validation report. There's no way to list the individual flagged entities, see why each was flagged, accept the ones that are actually fine, or fix the ones that aren't. The queue just sits there, keeping corpus validation at 422.

## What Changes

- Add an admin API to triage the `needsReview` queue: list (filter by book and reason, paged), inspect one, and resolve (accept-as-correct, or edit name/fields).
- Derive the flag **reason** on read — `ocr-artifact` vs `low-confidence` — via the existing OCR heuristic, so we don't need to persist the LLM confidence.
- Resolve is **canonical-backed**: it writes `books/canonical/<book>.json` (clear the flag and/or apply the edit), then re-projects only that one entity into Qdrant via a new targeted re-index — no full-book re-ingest, and Qdrant stays consistent with the canonical source of truth.
- Add bulk-accept to clear flags for a whole book/reason set at once (accepting many correct-but-low-confidence entities).
- Update `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` for the new endpoints.

## Capabilities

### New Capabilities

- `needsreview-triage`: admin endpoints to list/get/resolve flagged entities, reason derivation, and canonical-backed resolution with targeted single-entity re-index.

### Modified Capabilities

(none — additive)

## Impact

- Code: new `NeedsReviewService` (list/get/resolve over canonical via `CanonicalJsonLoader` + a canonical writer); a targeted `ReindexEntityAsync(bookId, entityId)` factored out of `EntityIngestionOrchestrator` (single-entity render→embed→upsert, preserving the book FileHash and `DeleteByFileHashExceptAsync` semantics so no other points are touched); a `NeedsReviewEndpoints` group.
- APIs: `GET /admin/entities/needs-review`, `GET /admin/entities/{id}`, `POST /admin/entities/{id}/resolve`, `POST /admin/entities/needs-review/accept`. `.http` + `.insomnia.json` updated.
- Data: resolutions edit canonical JSON (git-trackable) and update individual Qdrant points; entity counts unchanged by accept, may change names/fields on edit. No re-extraction.
- Out of scope: any Blazor admin UI (possible later spec on top of these endpoints).
