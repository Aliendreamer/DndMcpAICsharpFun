## 1. 5etools record index (TDD)

- [ ] 1.1 Write failing tests for a `FivetoolsRecordIndex` that builds an `id → EntityEnvelope` map from the local `5etools/` files via `FivetoolsSourceRegistry` + mappers: a known record (e.g. PHB Fireball) maps to the expected id; absent files yield an empty index (no throw); optional filtering to a set of source keys returns only those
- [ ] 1.2 Implement `FivetoolsRecordIndex` (reuse `FivetoolsSourceRegistry.AllEntries` + the existing mappers; read-only, no Qdrant); make tests green

## 2. Deep-merge `EntityMerger` (TDD)

- [ ] 2.1 Write failing `EntityMerger` tests: 5etools scalar wins for `fields.cr` (`"l/4"`→`"1/4"`); our `fields.entries` preserved when both differ; 5etools fills a field we lack (`components`); SRD flags from 5etools; keywords unioned; name = 5etools clean name unless `DataSource=="manual"` (manual preserved); purity (inputs unmutated)
- [ ] 2.2 Extend `EntityMerger.Merge` with a recursive `JsonElement` deep-merge using a per-entity-type narrative-key allowlist (`entries`, `description`, `text`, per-type prose keys) where canonical wins; 5etools wins other present/non-empty keys; add the `name` rule; keep flags/keywords/page/type rules; make tests green

## 3. Wire enrichment into ingest (TDD)

- [ ] 3.1 Write failing orchestrator-level test: an entity with a matching 5etools record is enriched (5etools structured field present, our entries preserved); an entity with no match is unchanged; no 5etools-only entity is added to the upsert set
- [ ] 3.2 In `EntityIngestionOrchestrator`, build/inject the `FivetoolsRecordIndex` (filtered to the book's source key), use the matching 5etools record as the enrichment `existing` for `EntityMerger`, and never add 5etools-only records; make tests green

## 4. Coverage reporting

- [ ] 4.1 Add `{ enriched, matchedFivetools, unmatched }` counts to the ingest result/log; assert them in a test

## 5. Build & suite

- [ ] 5.1 `dotnet build` clean (0 warnings) and `dotnet test` fully green, including existing `EntityMergerTests` and ingestion tests (update the existing `fields → canonical always wins` test to the new deep-merge behavior)

## 6. Apply to data

- [ ] 6.1 Re-ingest the four books (DMG=1, Tasha=2, PHB=3, MM=4); confirm `dnd_entities` count stays 2310 (no 5etools-only entities added) and review the reported enrichment coverage per book
- [ ] 6.2 Spot-check: a previously OCR-noisy structured field (e.g. a monster CR or spell level) now shows the clean 5etools value while the entity's prose is unchanged
- [ ] 6.3 Review and commit (no canonical JSON changes expected — enrichment is at ingest; commit code only)
