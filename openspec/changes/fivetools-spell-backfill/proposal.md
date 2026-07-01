## Why

Hybrid path to 361, the B fallback. Parse-grind (A) plateaued at 355/361 — iterations 1+2 recovered +20,
but the last 6 spells have OCR-damaged description entries (name+level not a clean block; Regenerate not
co-located at all) or a specific cleaning edge (Produce Flame declined as "PRODUCE FLAME Conjuration").
No reasonable parser rule reaches them. We already trust 5etools as the authority for the extraction gate,
and it holds the complete, accurate PHB spell data. Backfill the gaps deterministically to guarantee the
full set.

## What Changes

- **Add a 5etools authoritative spell backfill** (`POST /admin/books/{id}/backfill-spells`): for an
  official book (one with a `FivetoolsSourceKey`), for each 5etools spell of the book's source that is NOT
  already a Spell entity in the canonical, create a Spell entity from the 5etools record and append it.
  Idempotent — only fills gaps, never overwrites parsed entities. The mapping is near-direct because our
  Spell entity's content fields (`entries` as a "Description" block, `entriesHigherLevel`, `damageInflict`,
  `conditionInflict`) mirror the 5etools schema; the envelope (id via `EntityIdSlug.For`, name, sourceBook,
  edition, page) is standard. Backfilled entities are marked `dataSource:"5etools-backfill"` (auditable,
  distinct from parsed and hand-authored) and `disposition:Accepted`.

## Capabilities

### New Capabilities
- `fivetools-spell-backfill`: create canonical Spell entities for an official book's 5etools spells that
  the parser did not produce, so the spell set is complete and auditable.

## Impact

- Code: a new admin endpoint + a backfill service that reads the book's 5etools spells (reuse
  `FivetoolsRecordIndex`/`FivetoolsSourceRegistry`), diffs against the canonical, maps missing 5etools
  spells → Spell entities (`EntityIdSlug.For` for ids), and appends to `books/canonical/<slug>.json`.
  Update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` (new endpoint). Unit tests for the mapping +
  the gap-only behavior.
- Validation: run backfill on PHB → **361/361 spells**; the 6 gaps (Contact Other Plane, Hallucinatory
  Terrain, Prismatic Wall, Produce Flame, Ray of Sickness, Regenerate) present as `5etools-backfill`
  entities; parsed entities untouched; no dup ids; canonical validates.
- Non-goals: backfilling other entity types (Monster/Race/etc.) — spells only for now; the endpoint/service
  can generalize later.
