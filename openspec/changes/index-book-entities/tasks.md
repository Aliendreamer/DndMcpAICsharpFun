## 1. Pre-flight

- [ ] 1.1 Confirm stack is healthy (`app`, `ollama`, `qdrant`, `postgres`, `marker`) and qwen3:8b is pulled (`ollama-pull`)
- [ ] 1.2 Record current state: registry statuses, `dnd_blocks` count (expect 6,898), `dnd_entities` count (expect 0)
- [ ] 1.3 Resolve the two book ids from the registry (DMG, Tasha's)

## 2. Extract DMG (stuck — force)

- [ ] 2.1 `POST /admin/books/{dmg-id}/extract-entities?force=true` (long-running; resumes from checkpoint if interrupted)
- [ ] 2.2 Verify `books/canonical/<dmg-slug>.json` is produced; review sibling `<slug>.errors.json` / `<slug>.warnings.json` if present
- [ ] 2.3 Spot-check a sample of extracted entities for correctness

## 3. Extract Tasha's

- [ ] 3.1 `POST /admin/books/{tce-id}/extract-entities`
- [ ] 3.2 Verify `books/canonical/<tce-slug>.json` produced; review errors/warnings siblings

## 4. Validate + ingest

- [ ] 4.1 `POST /admin/canonical/validate` → expect 200 (clean); fix FAIL-class issues if 422
- [ ] 4.2 `POST /admin/books/{dmg-id}/ingest-entities`
- [ ] 4.3 `POST /admin/books/{tce-id}/ingest-entities`

## 5. Verify indexed

- [ ] 5.1 `dnd_entities` points_count > 0 in Qdrant
- [ ] 5.2 `GET /retrieval/entities/search` returns results for a known DMG and a known Tasha's entity
- [ ] 5.3 Confirm both books' registry status reaches `EntitiesIngested`
