> **Note:** This is an operational runbook — no code is written. Steps are executed against the running stack (`docker compose up`, host port 5101). `ADMIN_KEY` is the `Admin:ApiKey` value (from `Config/appsettings.Development.json` or the `Admin__ApiKey` env override). Each `curl` below assumes `-H "X-Admin-Key: $ADMIN_KEY"`.

## 1. Pre-flight (baseline + sanity)

- [ ] 1.1 Confirm the stack is healthy: `docker compose ps` shows `app`, `marker`, `ollama`, `qdrant`, `postgres` all `Up (healthy)`; `curl -s -o /dev/null -w '%{http_code}' http://localhost:5101/health` returns `200`.
- [ ] 1.2 Confirm the PDF is present: `books/System Reference Document.pdf` exists (~4.8 MB).
- [ ] 1.3 Record baseline Qdrant point counts for the isolation check: `dnd_blocks` and `dnd_entities` via `curl -s http://localhost:6333/collections/dnd_blocks` and `.../dnd_entities` (expect `dnd_blocks`≈17,571, `dnd_entities`≈2,310). Save both numbers.

## 2. Register the SRD (no 5etools key)

- [ ] 2.1 `POST /admin/books/register` with the PDF and `displayName="System Reference Document"`, **omitting** `fivetoolsSourceKey`. Capture the returned book `id` and confirm the derived slug is `system-reference-document`.
- [ ] 2.2 Confirm a new `IngestionRecord` row exists for the book with the correct `FilePath`. Do **not** run `ingest-blocks`.

## 3. Extract canonical JSON (the long step)

- [ ] 3.1 Kick off extraction: `POST /admin/books/{id}/extract-entities` (no `?force` on first run). Expect HTTP 202.
- [ ] 3.2 Monitor progress (multi-hour, qwen3-bound). Poll the book's status and/or watch for the checkpoint files `books/canonical/system-reference-document.progress.json` updating every 100 candidates. If the stack restarts mid-run, re-issue with `?force=true` — extraction resumes from the checkpoint.
- [ ] 3.3 On completion, confirm `books/canonical/system-reference-document.json` exists. If it is root-owned (written from inside the container), `chown` it to the host user so it can be read/committed.

## 4. Verify isolation + extraction quality

- [ ] 4.1 **Isolation check (the new requirement):** re-read `dnd_blocks` and `dnd_entities` point counts; both MUST equal the baselines from 1.3 — extraction wrote nothing to Qdrant.
- [ ] 4.2 Sanity-check the canonical JSON: entity count, `schemaVersion`, and the spread of entity types (`Class`/`Monster`/`Spell`/etc.). Note rough overlap with existing `phb14.*` / `mm14.*` / `dmg14.*`.
- [ ] 4.3 Review siblings if present: `system-reference-document.errors.json` (dropped/failed records) and `system-reference-document.warnings.json` (inter-book dangling refs). Summarize counts.

## 5. Review gate + decision (hard stop)

- [x] 5.1 Present a short summary: entity count by type, errors/warnings counts, observed overlap with existing books, and the confirmed-unchanged Qdrant counts.
- [x] 5.2 Record the go/no-go decision on what to do with the SRD entities (e.g. skip / SRD-flag-only / blocks-only / full ingest with dedup). The dedup/overlap strategy, if pursued, becomes its **own** openspec change — out of scope here.
- [x] 5.3 Stop. Do not run `ingest-entities` or `ingest-blocks` as part of this change.

## Outcome (2026-06-17)

Initial extraction yielded **0 entities** — the SRD PDF has no bookmarks and the candidate scanner was bookmark-gated. That root cause was fixed by the separate **`heading-derived-toc-fallback`** change (merged); re-running extraction on the fixed code then produced the result below.

- **1,494 entities** extracted (1 error, 10 warnings). Type breakdown: Monster 700, Spell 296, Item 254, Class 95, Trap 50, Plane 45, Race 21, Condition 17, God 9, Background 7.
- The 1 error: `use-magic-device` intra-book dangling ref → that entity excluded. The 10 warnings: inter-book cross-references (non-blocking).
- **Isolation verified:** `dnd_blocks=17571`, `dnd_entities=2309` unchanged — extraction wrote nothing to Qdrant.

**Decision: SHELVE.** The SRD is the open subset of the 2014 PHB/DMG/MM we already hold, so its 1,494 entities are near-total duplicates. We **commit the reviewed canonical JSON to git as a clean, openly-licensed record** but do **NOT** ingest into `dnd_entities`. Any future ingest must go through a dedicated dedup / SRD-flag change (the ~700 "monsters" also look over-segmented vs the real ~325 and would need review first).
