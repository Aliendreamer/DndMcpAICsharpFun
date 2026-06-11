## Context

The extraction/ingestion endpoints already exist (`POST /admin/books/{id}/extract-entities`, `/ingest-entities`, `POST /admin/canonical/validate`). The extraction pipeline writes `books/canonical/<slug>.json` plus optional sibling errors/warnings files, checkpoints every 100 candidates to `<slug>.progress*.json`, and is crash-resumable. The current blocker is operational state, not missing code: DMG is stuck in `EntitiesExtracting` with nothing on disk, Tasha is at `JsonIngested`, and `dnd_entities` is empty.

## Goals / Non-Goals

**Goals:**
- Populate `dnd_entities` for both registered books (DMG, Tasha).
- Recover the DMG's stuck status without bespoke recovery code, using `force=true`.
- Codify that `force=true` overrides any prior status.

**Non-Goals:**
- A startup stale-run detector or a manual reset endpoint (explicitly deferred — force flag is the chosen mechanism).
- Re-ingesting blocks (already complete at 6,898 points).
- Adding new books.

## Decisions

- **`force=true` as the recovery mechanism.** Rather than add reset code, the run uses the existing force flag, which overwrites canonical JSON and proceeds regardless of the record's current status. This keeps the change near-zero-code and leans on a path the pipeline already supports. The spec is updated to make this override behavior explicit and tested-by-contract.
- **Extract → review → ingest, per the documented runbook.** Canonical JSON is the hand-correctable source of truth; the run produces it, it is spot-checked, then ingested. Validation (`/admin/canonical/validate`) gates ingestion.
- **Sequential, not parallel.** Run one book at a time to avoid contending for the single Ollama instance; DMG first (force), then Tasha.

## Risks / Trade-offs

- [Multi-hour runtime] → Pipeline checkpoints every 100 candidates and is crash-resumable; a re-interruption resumes rather than restarts.
- [LLM extraction errors] → Canonical JSON is reviewed before ingestion; `/admin/canonical/validate` catches FAIL-class issues (duplicate IDs, schema mismatch).
- [`force` overwrites in-progress canonical JSON] → Acceptable here: DMG has no usable canonical JSON (none on disk), so nothing of value is lost.

## Open Questions

- None blocking. If extraction proves too slow to complete in one sitting, rely on checkpoint resume across runs.
