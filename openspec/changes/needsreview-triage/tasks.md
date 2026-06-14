## 1. Canonical writer (TDD)

- [ ] 1.1 Write failing tests for a `CanonicalJsonWriter`: load a `<book>.json`, clear `needsReview` on one entity by id, write back, reload, assert flag cleared and all other entities byte-stable; an edit sets `name`/merges `fields`
- [ ] 1.2 Implement `CanonicalJsonWriter` (reuse extraction's serializer settings + trailing newline; per-book file write lock); make tests green

## 2. Targeted re-index (TDD)

- [ ] 2.1 Write failing test: `ReindexEntityAsync(bookId, entityId)` upserts exactly one `EntityPoint` and does NOT call `DeleteByFileHashExceptAsync` (use a fake store recording calls)
- [ ] 2.2 Factor the per-entity load→merge→render→embed→single-upsert path out of `EntityIngestionOrchestrator` into `ReindexEntityAsync`; make tests green; ensure full-book ingest still uses the shared path

## 3. NeedsReviewService (TDD)

- [ ] 3.1 Write failing tests: list reads canonical, filters by `book`/`reason`, pages with `offset`/`limit`, derives `reason` via `HasOcrArtifacts`; get-one returns full entity; resolve `accept` clears flag + calls reindex; resolve `edit` applies name/fields + clears + reindex; bulk-accept clears a filtered set; unknown id → not found
- [ ] 3.2 Implement `NeedsReviewService` over `CanonicalJsonLoader` + `CanonicalJsonWriter` + `ReindexEntityAsync`; make tests green

## 4. Endpoints + contracts

- [ ] 4.1 Add `NeedsReviewEndpoints` under `/admin`: `GET /admin/entities/needs-review`, `GET /admin/entities/{id}`, `POST /admin/entities/{id}/resolve`, `POST /admin/entities/needs-review/accept`; wire DI; admin-key protected
- [ ] 4.2 Update `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` with example requests for all four endpoints (project rule)

## 5. Build & suite

- [ ] 5.1 `dotnet build` clean (0 warnings) and `dotnet test` fully green

## 6. Use it

- [ ] 6.1 Against the running stack: `GET /admin/entities/needs-review?book=phb14` lists flagged PHB entities with reasons; bulk-accept the clearly-fine low-confidence sets; spot-fix a couple of ocr-artifact ones via resolve:edit; confirm validation `needsReview` counts drop and the edited entities are searchable with corrected names
