## Why

Two ingestion pipelines — 5etools import and canonical PDF extraction — produce overlapping entities with mismatched IDs (wrong book prefix, wrong entity type slug) and complementary data (5etools has correct types/SRD/keywords; canonical has rich fields/canonicalText). The result is duplicate Qdrant points per entity, worse search quality, and no single authoritative record.

## What Changes

- `EntityIdSlug.BookOverrides` extended with source key aliases so both pipelines produce identical book-prefix slugs (`"TCE"` → `"tce"`, `"PHB"` → `"phb14"`, `"DMG"` → `"dmg14"`, etc.)
- New `POST /admin/canonical/fix-types?book=<slug>` endpoint — one-time type fixer that cross-references 5etools data by name+sourceBook, rewrites entity types and IDs in canonical JSON files, updates internal cross-references
- Entity extraction prompt updated to instruct the LLM to classify `EntityType` correctly from content (no more defaulting everything to `Class`)
- `IEntityVectorStore` gains `GetByIdsAsync` for batch pre-fetch before merge
- New `EntityMerger` service applies per-field priority merge (canonical wins on `fields`/`canonicalText`/`firstAppearedIn`; 5etools wins on `srd`/`srd52`/`basicRules2024`/`type`; best-of for `keywords`/`page`)
- `EntityIngestionOrchestrator` wires in fetch → merge → upsert flow on every `ingest-entities` run
- `QdrantEntityVectorStore.ToPoint` uses deterministic UUID derived from entity ID (no more random GUIDs)

## Capabilities

### New Capabilities

- `entity-id-unification`: Unified entity ID scheme across both pipelines using lowercase 5etools source key as book prefix and correct entity type slug
- `canonical-type-fixer`: Admin tooling to one-shot correct entity types and IDs in existing canonical JSON files using 5etools as type reference
- `entity-merge`: Per-field merge of 5etools and canonical data at ingest time, producing single authoritative Qdrant points with best data from both sources

### Modified Capabilities

- `entity-extraction-pipeline`: Extraction prompt updated — LLM must now classify correct `EntityType` per entity, not default to `Class`
- `entity-vector-store`: Deterministic point UUIDs replace random GUIDs; new `GetByIdsAsync` batch fetch method added
- `structured-entities`: Entity ID format changes — book prefix is now always lowercase source key (`tce`, `phb14`, `dmg14`) not display-name slug

## Impact

- `Domain/Entities/EntityIdSlug.cs` — BookOverrides extended
- `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs` — merge step added
- `Features/VectorStore/Entities/QdrantEntityVectorStore.cs` — deterministic UUIDs, GetByIdsAsync
- `Features/VectorStore/Entities/IEntityVectorStore.cs` — GetByIdsAsync interface
- `Features/Admin/` — new fix-types endpoint
- `Prompts/` — extraction system prompt updated
- `data/canonical/*.json` — entity IDs rewritten after fix-types runs
- All existing canonical ingestions must be re-run after this change
