## Why

Extraction (from the user's own books) is the source of truth for entities and hits ~99% on the core
books — but a class is a multi-page table, so extraction emits classes **prose-only**
(`fields:{entries:[…]}`, no `hd`/`classFeatures`/`subclassTitle`). The level-up feature needed those
structured fields and had nothing to ground on. The existing 5etools helper is **entity-level, roster-gap
only** (`EntityBackfillService` appends whole *missing* entities and *skips* ones already present) — so a
prose class that already exists never gets its structured fields filled. There is **no field-level
gap-fill** anywhere. This is `backfill-spells` grown up to the field level: extraction stays the
favorite; 5etools only patches the holes.

## What Changes

- **A field-level 5etools gap-fill** — for an entity extraction already produced, merge in **only the
  structured fields it is missing** from the matching 5etools record, into the **canonical** JSON, and
  **never** overwrite a field extraction produced or its prose (`entries`).
- **All object types**, driven by the existing `FivetoolsSourceRegistry` + `FivetoolsMapperRegistry`; the
  only per-type thing is a declarative **structured-field allowlist** (`type → {field names}`). Types
  extraction already fills well are a safe no-op.
- **Durable by construction** — the fill is merged into the canonical and **auto-runs at the end of
  `extract-entities`**. Because 5etools is static and the allowlist fixed, it's a deterministic
  re-derivation, so a `force` re-extract can't silently drop it (the anti-"we lost the path" guarantee).
- **A standalone `POST /admin/books/{id}/fill-fields`** to run it on an already-extracted book (for the
  existing core books and the cleanup below).
- **Cleanup of a wholesale `POST /admin/5etools/import` run** done earlier this session (it replaced 9868
  entities in Qdrant, clobbering the 99% extraction monsters/spells): fill the core books, then **rebuild
  `dnd_entities` from the canonical** — dropping the import's `dataSource:"5etools"` strays and restoring
  extraction entities + the newly field-filled classes.

## Capabilities

### New Capabilities

- `fivetools-field-fill`: an idempotent, type-agnostic field-level gap-fill that patches only allowlisted
  structured fields onto extraction canonical entities from the matching 5etools record — never
  overwriting extraction content — auto-run after extraction and re-runnable via an admin endpoint.

## Impact

- **New code**: `Features/Ingestion/FivetoolsIngestion/EntityFieldFillService.cs` + a per-type
  structured-field allowlist config; wiring the auto-run into the extract-entities completion; a new
  `POST /admin/books/{id}/fill-fields` admin endpoint.
- **Reused**: `FivetoolsSourceRegistry`, `FivetoolsMapperRegistry`/`BuildFields`, `EntityNameIndex.Normalize`,
  `CanonicalJsonLoader`/`CanonicalJsonWriter`, `IngestionRecord.FivetoolsSourceKey`, `EntityIdSlug`, the
  roster-backfill patterns.
- **HTTP**: new `fill-fields` route → **update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json`**
  in the same commit.
- **One-time cleanup** (not a permanent route): fill the core books + rebuild `dnd_entities`; any
  collection wipe is a `Tools/` console or a direct Qdrant op.
- **No** new persistence/migration/MCP tool.
- **Verification**: unit merge rules; idempotency (byte-identical re-run); canonical-rewrite gates
  (unique-id + `CanonicalJsonLoader` round-trip + `entries` untouched); a **real-5etools** spot-check that
  the allowlisted fields actually exist in the corpus; live — after fill + rebuild, level-up grounds all
  classes from **extraction** entities and a monster reads back as the extraction version.
