## 1. Pre-flight

- [x] 1.1 Confirm stack is healthy (`app`, `ollama`, `qdrant`, `postgres`, `marker`) and qwen3:8b is pulled (`ollama-pull`)
- [x] 1.2 Record current state: registry statuses, `dnd_blocks` count (6,898), `dnd_entities` count (0)
- [x] 1.3 Resolve the two book ids from the registry (DMG=1, Tasha's=2)

## 2. Extract DMG (stuck — force)

- [x] 2.1 `POST /admin/books/{dmg-id}/extract-entities?force=true` (long-running; resumes from checkpoint if interrupted)
- [x] 2.2 Verify `books/canonical/dmg14.json` is produced; review sibling `dmg14.errors.json` / `dmg14.warnings.json`
- [x] 2.3 Spot-check a sample of extracted entities for correctness

## 3. Extract Tasha's

- [x] 3.1 `POST /admin/books/{tce-id}/extract-entities` (resumed from checkpoint after an interruption)
- [x] 3.2 Verify `books/canonical/tce.json` produced; review warnings sibling

## 4. Validate + ingest

- [x] 4.1 `POST /admin/canonical/validate` → 422 with 0 FAIL-class failures (only NeedsReview flags on
      OCR/low-confidence entities, expected). Hand-corrected 47 garbled entity names before ingest.
- [x] 4.2 `POST /admin/books/{dmg-id}/ingest-entities`
- [x] 4.3 `POST /admin/books/{tce-id}/ingest-entities`

> During ingest, found and fixed two bugs (committed separately as fix(ingestion)): the orchestrator
> self-deleted its own batch (upsert then delete by the same FileHash), and EntityIngestionOptions
> read the wrong canonical directory (`data/canonical` vs `books/canonical`).

## 5. Verify indexed

- [x] 5.1 `dnd_entities` points_count = 1080 (672 DMG + 408 Tasha) in Qdrant
- [x] 5.2 `GET /retrieval/entities/search` returns results for known DMG ("Rod of Lordly Might") and Tasha ("Telekinetic Master") entities
- [x] 5.3 Both books' registry status reaches `EntitiesIngested`
