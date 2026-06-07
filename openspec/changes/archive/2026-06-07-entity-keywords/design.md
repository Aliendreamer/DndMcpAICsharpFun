## Context

The `keywords` Qdrant payload field and the `keyword` query-string filter exist in the codebase but are never populated, so all keyword searches return empty. Two ingestion paths need to fill this field:

1. **5etools import** — monsters in 5etools JSON have a `traitTags` array (e.g. `["Amphibious", "Pack Tactics"]`). Other entity types have analogous tag fields (`actionTags`, `spellcastingTags`) but traits are the most useful for filtering.
2. **LLM extraction from PDFs** — the current `MonsterFields` JSON schema has no `keywords` field, so the LLM never produces one.

`EntityEnvelope` currently has no `Keywords` property; the field only exists as a string constant in `EntityPayloadFields`.

## Goals / Non-Goals

**Goals:**

- `EntityEnvelope` carries `Keywords: IReadOnlyList<string>`
- 5etools `traitTags` → `Keywords` for Monster entities; other entity types get empty list for now
- Canonical JSON files can store and round-trip a `keywords` array
- `MonsterFields.schema.json` gains a `keywords` optional string array
- LLM extraction system prompts guide the model to populate `keywords` from trait names
- Qdrant `ToPoint` writes keywords; `ToEnvelope` reads them back; filter is functional
- Re-ingest 5etools after the change to populate existing index

**Non-Goals:**

- Populating keywords for non-Monster entity types from 5etools (Spell, Class, Item, etc.) — deferred
- Retroactively re-extracting existing canonical JSON files for keywords — the field will be empty for old extractions; only new extractions benefit
- Fuzzy / partial keyword matching — exact keyword match only (Qdrant keyword index)

## Decisions

**D1 — Add `Keywords` to `EntityEnvelope` as `IReadOnlyList<string>` with default `[]`**
Using a positional record default keeps it non-breaking for existing construction sites. Alternative: a separate side-channel (e.g. pass through `Fields` JSON) — rejected because it would bypass type-safety and the existing `ToPoint`/`ToEnvelope` symmetry.

**D2 — Source for 5etools monsters is `traitTags` only (not `actionTags`, `miscTags`)**
`traitTags` contains semantically meaningful creature traits (`Amphibious`, `Undead Fortitude`, `Pack Tactics`) that match user intent for keyword filtering. `actionTags` are action labels (`Multiattack`, `Breath Weapon`) and `miscTags` are coded abbreviations (`MW`, `AOE`) — both are less useful as search keywords. Can be extended later.

**D3 — LLM extraction schema: `keywords` is an optional `string[]`, populated from trait names**
The LLM already sees trait text; asking it to emit trait names as a flat keyword list is low-effort and high-value. Extraction system prompt addition: short instruction to collect creature trait names (e.g. `Pack Tactics`, `Sunlight Sensitivity`) into `keywords`.

**D4 — Qdrant stores keywords as a repeated keyword index (existing `keywords` payload field)**
`CreatePayloadIndexAsync` with `PayloadSchemaType.Keyword` already exists in `QdrantCollectionInitializer`. The filter uses `KW(EntityPayloadFields.Keywords, value)` which does exact match on one keyword. This is already implemented — we just need to populate the field.

**D5 — Re-ingest 5etools after code ships; do not auto-trigger**
Re-ingestion is a manual step (`POST /admin/5etools/import`) after deployment. No migration needed for DMG canonical JSON — it will get keywords on next LLM extraction.

## Risks / Trade-offs

- **Stale index after deploy** → Until `POST /admin/5etools/import` is re-run, the keyword filter still returns empty for 5etools entities. Mitigation: document in the tasks.
- **LLM keyword quality** → The LLM may produce inconsistent capitalisation or invent keywords not in the source text. Mitigation: prompt constrains keywords to trait/feature names visible in the stat block; reviewers can hand-correct canonical JSON.
- **Empty keywords for non-Monster 5etools types** → Spell, Class, Item searches with `?keyword=` will still return empty. Mitigation: Non-Goals; acceptable for this iteration.

## Migration Plan

1. Ship code changes (EntityEnvelope, mapper, Qdrant store, schema, system prompt)
2. `POST /admin/5etools/import` — re-populates dnd_entities with keywords for all 5etools monsters
3. Canonical JSON entities gain keywords only on future LLM extraction runs
4. No rollback complexity — adding a field is additive; old Qdrant points without `keywords` simply won't match keyword filter queries (same as today)
