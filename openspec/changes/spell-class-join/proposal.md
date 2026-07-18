## Why

The companion can't answer "which spells can a Wizard learn" — a spell↔class **join**. Our spell entities don't carry the relationship (only 3% have a non-empty `classes` field) because 5etools moved class info *off* the spell object into the generated reverse index `5etools/spells/sources.json` (`{ SOURCE: { SpellName: { class:[{name,source}], subclass:[…] } } }`). That index maps **801/920 spell-entries across all 10 caster classes** and is a static on-disk JSON — deterministic, no LLM, no GPU. So the one join in the deferred Bin-B work that needs **no prose extraction** is buildable now as a query-time join. (The other deferred pieces — race resistances (aggregation) and tables — genuinely need prose extraction and stay deferred.)

## What Changes

- Add a cached `SpellClassIndex` that loads `sources.json` once and answers "can class C cast spell (name, source)?".
- Extend the `list_entities` set query (from `entity-set-query`) with a `castableByClass` filter: scroll the matching spells, filter in-memory by the index, return the complete class-filtered set with the honest total-vs-returned semantics.
- Add the `castableByClass` param to the tool and the `GET /retrieval/entities/list` endpoint; update `.http`/`.insomnia`.

## Capabilities

### New Capabilities

- `spell-class-join`: a deterministic, query-time spell↔class join over `5etools/spells/sources.json`, exposed as a `castableByClass` filter on the complete-set entity query — no data migration, no re-ingest, no GPU.

### Modified Capabilities

<!-- none: additive — extends the entity-set-query filter surface; no existing spec requirement changes -->

## Impact

- New `SpellClassIndex` (+ DI singleton) reading `5etools/spells/sources.json`.
- `EntityRetrievalService.ListAsync` — a `castableByClass` branch (in-memory class filter over the scrolled spell set; total = class-filtered count).
- `EntitySearchQuery` gains `CastableByClass`; `DndMcpTools.list_entities` + `EntityRetrievalEndpoints` `/list` gain the param; `.http` + `.insomnia` updated.
- **No** ingestion / Qdrant payload / canonical-JSON / GPU changes — the relationship is read from `sources.json` at query time.
- **Out of scope:** race-attribute aggregation and table extraction (need prose extraction; deferred).
