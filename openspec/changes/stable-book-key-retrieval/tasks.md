## 1. BookCatalog + consistency guard (no behavior change)

- [ ] 1.1 Create `Features/Retrieval/BookCatalog.cs` — immutable `(Key, DisplayName, Version, FivetoolsSourceKey)` entries for the five books: `PHB`/`PlayerHandbook 2014`, `MM`/`Monster Manual 2014`, `DMG`/`Dungeon Master's Guide 2014`, `XGE`/`Xanathar's Guide to Everything`, `ERLW`/`Eberron: Rising from the Last War` (display names VERBATIM from the live facet). Expose `Keys`, `DisplayNames`, `DisplayNameToKey`, `KeyToDisplayName`.
- [ ] 1.2 Unit test `BookCatalogTests`: `DisplayNameToKey`/`KeyToDisplayName` round-trip; keys are unique; no null/empty.
- [ ] 1.3 Run the test; commit.

## 2. source_key payload field written at ingest (no filter change yet)

- [ ] 2.1 Add `SourceKey = "source_key"` to `QdrantPayloadFields`. Add `string? SourceKey` to `BlockMetadata`.
- [ ] 2.2 In `BlockIngestionOrchestrator.IngestBlocksAsync`, set `SourceKey: record.FivetoolsSourceKey` on each `BlockMetadata`.
- [ ] 2.3 In `QdrantPayloadMapper`, write `source_key` into the point payload (skip when null).
- [ ] 2.4 In `QdrantCollectionInitializer.CreatePayloadIndexes`, add a keyword payload index on `source_key` for `dnd_blocks` (mirror the `source_book` index).
- [ ] 2.5 Test: a block ingested from a record with `FivetoolsSourceKey="DMG"` produces a payload with `source_key="DMG"` (unit test the mapper/metadata path; extend the existing block-ingestion test if present).
- [ ] 2.6 Build 0/0, full suite green; commit.

## 3. Vector-store facet + set-payload-by-book

- [ ] 3.1 Add to `IVectorStoreService`: `Task<IReadOnlyDictionary<string,long>> GetSourceKeyCountsAsync(ct)`, `Task<IReadOnlyDictionary<string,long>> GetSourceBookCountsAsync(ct)` (Qdrant facet on `dnd_blocks` by `source_key`/`source_book`), and `Task<long> SetSourceKeyForBookAsync(string displayName, string key, ct)` (Qdrant `set-payload` `source_key=key` where `source_book==displayName`, returns updated count).
- [ ] 3.2 Implement in `QdrantVectorStoreService` using the Qdrant facet + set-payload APIs.
- [ ] 3.3 Integration test (Testcontainers Qdrant): seed blocks with two display names → `GetSourceBookCountsAsync` returns correct counts; `SetSourceKeyForBookAsync` sets `source_key` and `GetSourceKeyCountsAsync` reflects it.
- [ ] 3.4 Full suite green; commit.

## 4. Backfill endpoint

- [ ] 4.1 `Features/Admin/RetrievalBackfillService` (or extend existing admin service): for each `BookCatalog` entry, call `SetSourceKeyForBookAsync(displayName, key)`; return per-book counts. Idempotent.
- [ ] 4.2 `POST /admin/retrieval/backfill-source-keys` (admin-key guarded) returning `{ perBook: {key: count}, total }`.
- [ ] 4.3 Update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json` with the new endpoint (admin key header).
- [ ] 4.4 Integration test (Testcontainers Qdrant): seed PHB+MM blocks (no source_key) → run backfill → every block has the right `source_key`, per-book counts equal the facet counts.
- [ ] 4.5 Full suite green; commit.

## 5. Switch retrieval scoping to source_key (AFTER backfill exists)

- [ ] 5.1 Rename `RetrievalQuery.SourceBooks` → `SourceKeys`. In `RagRetrievalService` build the single-`SourceKey`/`should`-OR conditions on `QdrantPayloadFields.SourceKey` instead of `SourceBook`.
- [ ] 5.2 `RuleSources.Books` → `RuleSources.Keys = ["PHB","DMG"]`; `DowntimeSources` → `["XGE","DMG"]`; `SettingCatalog.Core` and per-setting lists → `BookCatalog` keys. Update the three callers (`RulesAdjudicationService`, `DowntimeService`, `SettingLoreService`) to pass keys.
- [ ] 5.3 Update/rewrite the existing scope tests (setting-lore non-vacuity, rules/downtime scope) to assert the filter is built on `source_key` and to seed blocks with `source_key` (they must FAIL on the old display-name filter and PASS on the new one — behavior-change discrimination).
- [ ] 5.4 Full suite green; commit.

## 6. Scope-health startup guard

- [ ] 6.1 `ScopeHealthCheck` hosted startup step: after the Qdrant collection is ready, call `GetSourceKeyCountsAsync`; for every key in `RuleSources ∪ DowntimeSources ∪ SettingCatalog`, log `WARNING` (non-fatal) if count is 0. Register in `Program.cs` AND the `FullContainerScopeValidationTests` replica.
- [ ] 6.2 Unit test: with a fake facet missing `DMG`, a captured logger records a WARNING naming `DMG`; consistency test that every scope key ∈ `BookCatalog.Keys`.
- [ ] 6.3 Full suite green; commit.

## 7. Metadata-only registry reconcile

- [ ] 7.1 `Features/Ingestion/RegistryReconcileService`: facet `source_book`; for each display name with no `IngestionRecord`, insert one (`DisplayName`, `ChunkCount` from facet, `EntityCount` from `dnd_entities` facet, `Status=EntitiesIngested`, `Version`+`FivetoolsSourceKey` from `BookCatalog`, `FilePath`/`FileHash` best-effort by hashing on-disk PDFs). Never modify/delete existing. Return created list.
- [ ] 7.2 `POST /admin/books/reconcile` (admin-key guarded). Update `.http` + `.insomnia.json`.
- [ ] 7.3 Integration test (Testcontainers Postgres + Qdrant): seed MM blocks + a PHB record → reconcile creates the MM record with the facet count and leaves PHB untouched; a second run is a no-op.
- [ ] 7.4 Full suite green; commit.

## 8. Operational deploy + DMG ingest + verification (controller, not a subagent)

- [ ] 8.1 Rebuild the app image (`docker compose up -d --build app`); wait healthy. **Runbook order matters** (avoids mid-migration unmatched blocks): (a) `POST /admin/retrieval/backfill-source-keys` — verify per-book counts equal the facet block counts; (b) `POST /admin/books/reconcile` — verify MM/XGE/ERLW records created.
- [ ] 8.2 DMG ingest: identify the DMG PDF on disk (confirm it has bookmarks; note whether its MinerU conversion is cached vs a slow re-run); register it (`fivetoolsSourceKey=DMG`, `displayName="Dungeon Master's Guide 2014"`); `POST /admin/books/{id}/ingest-blocks`; poll to completion.
- [ ] 8.3 Verify: `GetSourceKeyCountsAsync`/facet shows `DMG > 0`; the DMG `source_book` equals `BookCatalog`'s `"Dungeon Master's Guide 2014"` exactly (fix registration + re-ingest if not).
- [ ] 8.4 Live smoke (Playwright chat, `test`/`test`): a DMG-covered rules question via `ask_rules` and a downtime question via `plan_downtime` each retrieve and CITE the DMG. Confirm the startup log no longer WARNs about `DMG` having 0 blocks.
- [ ] 8.5 If the smoke surfaces a durable lesson, add it to `.claude/skills/dev-flow/SKILL.md`.
