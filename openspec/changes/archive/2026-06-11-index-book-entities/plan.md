# Index Book Entities (DMG + Tasha) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. This is primarily an **operational** plan against the live stack plus one small spec-backed test.

**Goal:** Populate `dnd_entities` for both registered books by running the existing Ollama extraction + ingestion endpoints, using `?force=true` to override the DMG's stuck `EntitiesExtracting` status.

**Architecture:** No new feature code. The endpoints (`/admin/books/{id}/extract-entities`, `/ingest-entities`, `/admin/canonical/validate`) and the crash-resumable, checkpointing pipeline already exist. The only code touched is an assertion-level test confirming `force=true` ignores prior status. Everything else is operational execution and verification.

**Tech Stack:** ASP.NET admin API, Ollama qwen3:8b (local), Qdrant (`dnd_entities`), Postgres (`IngestionRecords`).

**⚠️ Runtime:** qwen3:8b is ~20–65 s/candidate → the DMG run is multi-hour. The pipeline checkpoints every 100 candidates to `books/canonical/<slug>.progress*.json` and resumes on retry, so interruptions are safe.

---

## File Structure

- (verify-only) `Features/.../*Extraction*` — confirm `force` path ignores status; no change expected
- Test: a focused test asserting `force=true` proceeds regardless of `IngestionStatus` (location: alongside existing extraction tests under `DndMcpAICsharpFun.Tests/`)

---

### Task 1: Pre-flight state capture

- [ ] **Step 1: Confirm stack health**

Run: `docker ps --format '{{.Names}}\t{{.Status}}'`
Expected: `app`, `ollama`, `qdrant`, `postgres`, `marker` all healthy.

- [ ] **Step 2: Confirm qwen3:8b is pulled**

Run: `docker exec dndmcpaicsharpfun-ollama-1 ollama list`
Expected: `qwen3:8b` present. If absent: `POST /admin/ollama-pull` (or `docker exec … ollama pull qwen3:8b`).

- [ ] **Step 3: Record baseline counts**

Run:
```bash
docker exec dndmcpaicsharpfun-postgres-1 psql -U dnd -d dnd -P pager=off \
  -c 'SELECT "Id","DisplayName","Status" FROM "IngestionRecords" ORDER BY "DisplayName";'
curl -s http://localhost:6333/collections/dnd_blocks  | grep -o '"points_count":[0-9]*'
curl -s http://localhost:6333/collections/dnd_entities | grep -o '"points_count":[0-9]*'
```
Expected: 2 records (DMG=`EntitiesExtracting`, Tasha=`JsonIngested`), `dnd_blocks`≈6898, `dnd_entities`=0. **Note the two book Ids** for later steps.

---

### Task 2: Confirm force-overrides-status contract (TDD-lite)

**Files:**
- Test: add under `DndMcpAICsharpFun.Tests/` next to the extraction-endpoint/handler tests

- [ ] **Step 1: Locate the extract-entities handler and its force handling**

Run: `grep -rn "extract-entities" --include=*.cs . | grep -v bin/ | grep -v obj/`
Read the handler; confirm where `force` is read and whether it short-circuits on existing canonical JSON / current status.

- [ ] **Step 2: Add/adjust a test asserting force ignores a stuck status**

Write a test that drives the handler (or its service) with a record in `IngestionStatus.EntitiesExtracting` and `force=true`, asserting it proceeds (does not early-return "already extracting"). Follow the existing extraction test's construction. If the handler already unconditionally proceeds on `force=true`, the test simply documents/locks that behavior.

- [ ] **Step 3: Run the test**

Run: `dotnet test --filter "FullyQualifiedName~Extract"`
Expected: PASS. If it FAILS because the handler blocks on `EntitiesExtracting` even with force, make the minimal handler change so `force=true` bypasses the status guard, then re-run.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test(extraction): force=true overrides a stuck EntitiesExtracting status"
```

---

### Task 3: Extract DMG (force)

- [ ] **Step 1: Kick off extraction (long-running)**

Run (substitute `{dmg-id}`):
```bash
curl -s -X POST "http://localhost:5101/admin/books/{dmg-id}/extract-entities?force=true" \
  -H "X-Api-Key: <Mcp:ApiKey>"
```
(Check `DndMcpAICsharpFun.http` for the exact admin auth header/value.) Run in the background; monitor `books/canonical/<dmg-slug>.progress.json` advancing.

- [ ] **Step 2: Wait for completion**

Poll until `books/canonical/<dmg-slug>.json` exists and the `.progress.json` checkpoint is gone (deleted on success). If the run dies, re-issue the same call — it resumes from the checkpoint.

- [ ] **Step 3: Review extraction output**

Inspect `books/canonical/<dmg-slug>.json` plus any `<slug>.errors.json` / `<slug>.warnings.json`. Spot-check several entities for correctness (this is the PR-review gate the runbook calls for).

---

### Task 4: Extract Tasha's

- [ ] **Step 1: Kick off extraction**

```bash
curl -s -X POST "http://localhost:5101/admin/books/{tce-id}/extract-entities" \
  -H "X-Api-Key: <Mcp:ApiKey>"
```

- [ ] **Step 2: Wait + review**

Confirm `books/canonical/<tce-slug>.json` produced; review errors/warnings siblings and spot-check entities.

---

### Task 5: Validate + ingest

- [ ] **Step 1: Corpus validation**

```bash
curl -s -X POST "http://localhost:5101/admin/canonical/validate" -H "X-Api-Key: <Mcp:ApiKey>" -i | head -1
```
Expected: `200`. If `422`, read the FAIL-class issues (duplicate IDs across files, schema-version mismatch), hand-correct the canonical JSON, re-run.

- [ ] **Step 2: Ingest entities for both books**

```bash
curl -s -X POST "http://localhost:5101/admin/books/{dmg-id}/ingest-entities" -H "X-Api-Key: <Mcp:ApiKey>"
curl -s -X POST "http://localhost:5101/admin/books/{tce-id}/ingest-entities" -H "X-Api-Key: <Mcp:ApiKey>"
```

---

### Task 6: Verify indexed

- [ ] **Step 1: Entity count > 0**

Run: `curl -s http://localhost:6333/collections/dnd_entities | grep -o '"points_count":[0-9]*'`
Expected: a non-zero count.

- [ ] **Step 2: Entity search returns results**

```bash
curl -s "http://localhost:5101/retrieval/entities/search?query=beholder"
curl -s "http://localhost:5101/retrieval/entities/search?query=Tasha"
```
Expected: results for a known DMG entity and a known Tasha's entity.

- [ ] **Step 3: Registry status reached terminal state**

Run the Postgres query from Task 1 Step 3.
Expected: both books at `EntitiesIngested`.

- [ ] **Step 4: Commit produced canonical JSON**

```bash
git add books/canonical/
git commit -m "data(canonical): extracted entities for DMG and Tasha's"
```
(Review the diff first — canonical JSON is the hand-correctable source of truth.)
