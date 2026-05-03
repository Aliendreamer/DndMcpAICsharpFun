## 1. Entity model & schemas (foundation)

- [ ] 1.1 Add `Domain/Entities/EntityType.cs` enum covering all 17 entity types
- [ ] 1.2 Add `Domain/Entities/EntityEnvelope.cs` record for the common envelope (id, type, name, sourceBook, edition, page, firstAppearedIn, revisedIn, settingTags, canonicalText, fields)
- [ ] 1.3 Add `Domain/Entities/Provenance.cs` records (`FirstAppearance`, `Revision`)
- [ ] 1.4 Add per-type `fields` records under `Domain/Entities/Fields/` — start with `ClassFields.cs` and `MonsterFields.cs` (the two ratified during brainstorming)
- [ ] 1.5 Add remaining 15 per-type field records: SubclassFields, RaceFields, SubraceFields, BackgroundFields, FeatFields, SpellFields, WeaponFields, ArmorFields, ItemFields, MagicItemFields, TrapFields, DiseasePoisonFields, VehicleMountFields, GodFields, PlaneFields, FactionFields, LocationFields, ConditionFields
- [ ] 1.6 Add `Domain/Entities/Spellcasting.cs` shared block (used by both Class and Monster)
- [ ] 1.7 Implement deterministic slug generator `EntityIdSlug` covering ASCII-folding + kebab-case + uniqueness
- [ ] 1.8 Add JSON-schema generation for each per-type fields record (output to `Schemas/canonical/<type>.schema.json` for prompt-time + load-time validation)
- [ ] 1.9 Add `Domain/Entities/CanonicalJsonFile.cs` envelope (schemaVersion, book metadata, entities[])
- [ ] 1.10 Write unit tests for `EntityIdSlug` covering same-input idempotency, ASCII folding, uniqueness checks

## 2. Canonical JSON load + validate

- [ ] 2.1 Add `Features/Entities/CanonicalJsonLoader.cs` that reads `data/canonical/<book>.json`, validates `schemaVersion`, deserialises against the per-type schemas
- [ ] 2.2 Implement reference-resolution validator that flags dangling cross-entity references as warnings
- [ ] 2.3 Implement duplicate-id detector that fails loading on collisions
- [ ] 2.4 Add `Features/Entities/CanonicalText/` per-type renderers that produce deterministic `canonicalText` from a typed `fields` block (Class, Monster first; remaining types follow)
- [ ] 2.5 Round-trip test: render canonicalText → embed in entity → re-render → byte-identical
- [ ] 2.6 Hand-write a tiny fixture canonical JSON (1 Class, 1 Monster, 1 Spell) for tests; commit under `Tests/Fixtures/canonical/`
- [ ] 2.7 Tests: load fixture, verify all envelope fields parsed, verify `fields` blocks parsed correctly per type

## 3. Entity vector store (Qdrant collection + indexes)

- [ ] 3.1 Add `Qdrant:EntitiesCollectionName` config key (default `dnd_entities`)
- [ ] 3.2 Extend Qdrant bootstrap to create the entity collection with the same vector dimension as `dnd_blocks`
- [ ] 3.3 Add payload-index creation for: `type`, `sourceBook`, `edition`, `bookType`, `settingTags`, `keywords`, `crNumeric`, `spellLevel`, `damageType`
- [ ] 3.4 Add `Infrastructure/Qdrant/EntityPayloadFields.cs` constants
- [ ] 3.5 Add `Features/VectorStore/Entities/IEntityVectorStore.cs` + Qdrant-backed implementation supporting upsert, delete-by-book-hash, get-by-id, vector-search-with-filters
- [ ] 3.6 Tests: bootstrap creates both collections + indexes; upsert/delete is idempotent

## 4. Entity ingestion pipeline

- [ ] 4.1 Extend `IngestionWorkItem` with `IngestEntities` work-item type
- [ ] 4.2 Add `Features/Ingestion/Entities/IEntityIngestionOrchestrator.cs` + implementation that loads canonical JSON, embeds each `canonicalText`, upserts to entity collection, marks status `EntitiesIngested`
- [ ] 4.3 Wire `IngestionQueueWorker` to dispatch `IngestEntities` items
- [ ] 4.4 Add `POST /admin/books/{id}/ingest-entities` endpoint per the entity-vector-store spec
- [ ] 4.5 Re-ingestion idempotency: delete prior entity points by `(sourceBook, hash)` before upserting
- [ ] 4.6 Extend `IngestionStatus` (or parallel sub-status) for entity-ingestion phases
- [ ] 4.7 Integration tests covering: enqueue success (202), unknown book (404), processing book (409), missing canonical JSON (404), idempotency

## 5. LLM extraction pipeline

- [ ] 5.1 Add `Features/Ingestion/EntityExtraction/IEntityExtractionOrchestrator.cs` + skeleton implementation
- [ ] 5.2 Add `IEntityExtractionLlmClient.cs` abstraction (decouples Ollama vs remote LLM choice; impl decision deferred per design Open Questions)
- [ ] 5.3 Implement Docling-output reuse path: extractor reads cached/persisted blocks rather than re-running layout
- [ ] 5.4 Implement schema-constrained per-type extraction: pass per-type JSON schema to the LLM prompt; validate output against schema; retry on failure within bounded budget
- [ ] 5.5 Implement errors-file path: schema-failing outputs go to `data/canonical/<book>.errors.json`
- [ ] 5.6 Implement atomic-write canonical JSON path: write to temp file, rename on success
- [ ] 5.7 Implement reference-resolution post-pass that emits warnings for dangling refs
- [ ] 5.8 Implement progress + summary logging at the cadence required by the extraction spec
- [ ] 5.9 Extend `IngestionWorkItem` with `ExtractEntities` work-item type; wire dispatch in `IngestionQueueWorker`
- [ ] 5.10 Add `POST /admin/books/{id}/extract-entities` endpoint (with `?force=true` parameter for re-extraction)
- [ ] 5.11 Integration tests: 202 on enqueue, 409 without `force` on existing JSON, atomic write, error-file produced on schema violations
- [ ] 5.12 End-to-end manual test on a small book section (e.g. one chapter from PHB) to validate extraction quality before backfilling

## 6. Retrieval endpoints (entity-aware)

- [ ] 6.1 Add `Features/Retrieval/Entities/EntityRetrievalEndpoints.cs`
- [ ] 6.2 Implement `GET /retrieval/entities/{id}` returning the full envelope + `fields` block; 404 on miss
- [ ] 6.3 Implement `GET /retrieval/entities/search` with vector search + structured filters (type, sourceBook, edition, bookType, settingTag, keyword, crNumeric_lte/gte, spellLevel, damageType)
- [ ] 6.4 Implement `GET /admin/retrieval/entities/search` (admin diagnostic) that includes pointId + full `fields`
- [ ] 6.5 Apply default + max topK consistent with the existing block-retrieval policy
- [ ] 6.6 Update `DndMcpAICsharpFun.http` with example requests for every new endpoint (per CLAUDE.md rule)
- [ ] 6.7 Integration tests covering ID lookup (hit/miss), vector search ordering, each filter individually, filters composing, unknown filter values dropped

## 7. Book deletion extension

- [ ] 7.1 Extend `DELETE /admin/books/{id}` to additionally delete entity points from `dnd_entities` (by hash) and delete the canonical JSON file from disk
- [ ] 7.2 Tests: deletion of a book that was both block- and entity-ingested removes everything; deletion of a block-only book is unaffected (no canonical JSON, no entity points)

## 8. End-to-end validation

- [ ] 8.1 Run the full pipeline on one real book (e.g. Player's Handbook 2014 or a single chapter as a smoke test): block ingestion → entity extraction → JSON review/correction → entity ingestion → retrieval test
- [ ] 8.2 Verify each of the five canonical example queries returns coherent results: cleric/warrior multiclass feats, 3 amphibian monsters for level-5 party, Eberron gods by influence, swashbuckler rogue plan, "which book introduced the artificer"
- [ ] 8.3 Document the new operator workflow ("register → block-ingest → entity-extract → review JSON in PR → entity-ingest") in README and CLAUDE.md
- [ ] 8.4 Confirm `DndMcpAICsharpFun.http` covers every new endpoint with realistic example requests

## 9. Migration & rollout

- [ ] 9.1 Backfill canonical JSON for the existing registered books (one at a time, hand-correcting in PR review)
- [ ] 9.2 After each backfill, run entity ingestion against the corrected JSON
- [ ] 9.3 Verify retrieval continues to function correctly across both collections after each backfill step
