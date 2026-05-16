# Design: Docling Disk Cache + Selective Error Re-extraction

**Date:** 2026-05-06  
**Status:** Approved  
**Change series:** structured-entity-extraction (post-Plan 2 amendment)

---

## Problem

1. `extract-entities` re-runs Docling on every call, including retries. A large book (PHB ~900-page PDF) takes 20+ minutes in Docling. If the LLM extraction crashes or produces failures, the operator must pay the full Docling cost again even though the PDF hasn't changed.

2. After a completed extraction, failed entities (LLM truncation, JSON parse error, missing schema) land in `<slug>.errors.json` with no way to retry just those candidates — the only option is `?force=true` which re-runs everything.

---

## Goals

- Cache Docling output to disk, keyed by file hash. Re-runs on the same PDF skip Docling entirely.
- Add `?errorsOnly=true` to `POST /admin/books/{id}/extract-entities`: re-run LLM only for candidates that appear in `<slug>.errors.json`, merge results into existing `<slug>.json`.
- Record `no_schema` skips in `errors.json` so they are visible and retryable if a schema is later added.

---

## Architecture

### DoclingDiskCache

A caching decorator that wraps `IDoclingPdfConverter`. Registered in DI in place of the bare `DoclingPdfConverter`; `DoclingPdfConverter` becomes an inner dependency.

**Cache key:** SHA-256 of the PDF file bytes — stable across renames and moves.  
**Cache location:** `data/docling-cache/<hash>.json` (configurable via `EntityExtractionOptions.DoclingCacheDirectory`).  
**Write strategy:** atomic temp-file + rename (same pattern as `CanonicalJsonWriter`).

Flow:
1. Hash the file.
2. If `<hash>.json` exists → deserialise and return.
3. If not → call inner converter, serialise result to `<hash>.json`, return result.

Error handling:
- Corrupt cache file (`JsonException` on read) → delete file, call through to real Docling, re-cache.
- Cache write failure → log warning at `Warning` level, return the result anyway (degraded, not broken).

In production docker-compose, `data/docling-cache/` should be backed by a named Docker volume so it survives container rebuilds.

### `errorsOnly` flag

**Trigger:** `?errorsOnly=true` on `POST /admin/books/{id}/extract-entities`.

**Pre-conditions (checked before anything else):**
- `<slug>.json` must exist — if not, throw `InvalidOperationException`: *"No canonical JSON found for {bookSlug}; run full extraction first."*
- `<slug>.errors.json` must exist and be non-empty — if not, log `"No errors file found for {bookSlug}; nothing to retry."` and return early (200).

**Flow:**
1. Load `<slug>.errors.json` → build `HashSet<string> retrySet` from `SourceEntityId`.
2. Call `ConvertAsync` → cache hit, returns instantly.
3. Scan candidates (same scanner as full extraction).
4. For each candidate: skip if ID not in `retrySet`.
5. LLM-extract only the retried candidates.
6. Load existing `<slug>.json` → append newly-successful envelopes → write back.
7. Write new `<slug>.errors.json` (candidates that still failed) and `<slug>.warnings.json`.

**No checkpoint files** are written for `errorsOnly` runs — the set of candidates is small and the run is short.

### `no_schema` tracking

The "no schema for type" branch in the extraction loop currently logs a warning and silently skips. It will now also write to `extractionErrors` with `ErrorKind: "no_schema"`, making the candidate visible in `errors.json` and retryable once the schema file is added.

---

## File Changes

| File | Change |
| --- | --- |
| `Features/Ingestion/Pdf/DoclingDiskCache.cs` | New — `IDoclingPdfConverter` caching decorator |
| `Features/Ingestion/EntityExtraction/EntityExtractionOptions.cs` | Add `DoclingCacheDirectory = "data/docling-cache"` |
| `Config/appsettings.json` | Add `DoclingCacheDirectory` to `EntityExtraction` section |
| `Extensions/ServiceCollectionExtensions.cs` | Wrap `DoclingPdfConverter` with `DoclingDiskCache` in DI |
| `Features/Ingestion/EntityExtraction/IEntityExtractionOrchestrator.cs` | Add `bool errorsOnly` param to `ExtractAsync` |
| `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` | `errorsOnly` branch; `no_schema` → errors file; merge on success |
| `Features/Admin/BooksAdminEndpoints.cs` | Add `?errorsOnly` query param, pass to orchestrator |
| `DndMcpAICsharpFun.http` | Add example request with `?errorsOnly=true` |

### Tests

| File | What it tests |
| --- | --- |
| `DndMcpAICsharpFun.Tests/Ingestion/Pdf/DoclingDiskCacheTests.cs` | Cache miss calls through; cache hit skips converter; corrupt file recovers |
| `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/EntityExtractionOrchestratorTests.cs` | `errorsOnly` skips non-error candidates; merges into existing JSON; `no_schema` written to errors |
| `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs` | `?errorsOnly=true` passed through to orchestrator |

---

## Configuration

```json
"EntityExtraction": {
  "DoclingCacheDirectory": "data/docling-cache",
  ...
}
```

Production docker-compose should mount `data/docling-cache` as a named volume alongside `qdrant_data` and `ollama_data`.

---

## Non-goals

- No cache eviction or TTL — the cache is keyed by file hash; a new PDF version gets a new hash automatically.
- No UI to inspect or clear cache entries — use the filesystem directly.
- No partial Docling re-runs — cache granularity is per book, not per page.
