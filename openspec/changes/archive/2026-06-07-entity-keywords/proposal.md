## Why

The `keyword` filter on entity search endpoints is dead code — the `keywords` Qdrant payload field is never populated, so filtering by it always returns zero results. 5etools source data includes rich `traitTags` (e.g. `Amphibious`, `Pack Tactics`, `Undead Fortitude`) that should power this filter, and the LLM extraction schema needs a matching `keywords` field so canonical-JSON-ingested entities are consistent.

## What Changes

- Add `IReadOnlyList<string> Keywords` to `EntityEnvelope` so keywords flow through the whole pipeline
- Map 5etools `traitTags` → `keywords` in `FivetoolsMonsterMapper` (and propagate to other entity mappers where equivalent tag fields exist)
- Write `keywords` to Qdrant payload in `QdrantEntityVectorStore.ToPoint`; read it back in `ToEnvelope`
- Add `keywords` to the LLM extraction schema (`MonsterFields` JSON schema + system prompt guidance) so future extractions produce the field
- Add `keywords` to `CanonicalJsonLoader` / `FivetoolsMapperBase` read path so it is picked up from canonical JSON files
- The `keyword` query param and `EntityFilters.Keyword` already exist — they become functional once the payload is populated
- Update `.http` and `insomnia.json` example requests to reflect the now-working filter
- Re-ingest 5etools data so `dnd_entities` is populated with keywords

## Capabilities

### New Capabilities

- `entity-keywords`: Keywords field on entities — populated from 5etools `traitTags` and LLM extraction, indexed in Qdrant, filterable via `?keyword=` query param

### Modified Capabilities

- `entity-extraction-pipeline`: Extraction schema gains a `keywords` array field; system prompt guidance updated to populate it
- `entity-vector-store`: `ToPoint` writes keywords payload; `ToEnvelope` reads it back; filter condition is now active
- `structured-entities`: `EntityEnvelope` gains `Keywords` field; canonical JSON schema updated

## Impact

- `Domain/Entities/EntityEnvelope.cs` — new `Keywords` field
- `Features/Ingestion/FivetoolsIngestion/FivetoolsMonsterMapper.cs` — map `traitTags`
- `Features/VectorStore/Entities/QdrantEntityVectorStore.cs` — write/read keywords payload
- `Features/Entities/CanonicalJsonLoader.cs` — read keywords from canonical JSON
- `Schemas/canonical/MonsterFields.schema.json` — add `keywords` array
- `Features/Ingestion/EntityExtraction/` system prompts — guidance on keywords population
- `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` — updated examples
- Requires re-ingesting 5etools data (`POST /admin/5etools/import`) to populate existing index
