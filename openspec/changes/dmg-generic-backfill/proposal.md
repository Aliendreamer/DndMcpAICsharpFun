## Why

The Dungeon Master's Guide is the last core-2014 book without PHB/MM-quality entity coverage: its `dmg14.json` was extracted before the allowlist gate, stat-line strip, and 5etools recall matcher landed (it still carries ~14 bogus "Class" entities and several unrecovered extraction failures), and DMG is dominated by 289 MagicItems that have no recall or backfill safety net. The 5etools recall+backfill machinery only exists for Monster and Spell, as two near-duplicate hand-rolled services — extending it one more time by copy-paste is the wrong move.

## What Changes

- **Fresh DMG re-extraction** on the current pipeline (`extract-entities?force=true`): allowlist gate removes chapter-noise "Class" entities, stat-line strip + recall matcher clean names, `errorsOnly` retry recovers the recorded extraction failures.
- **Generic backfill engine**: extract the common recall/diff/flag core (normalize → present/missing/extra → otherSource/unknown split → gap-only append → flag-unknown) into one `EntityBackfillService`, driven by a per-type provider that supplies only what differs (5etools source files, JSON array key, and the curated field projection).
- **Four providers**: Monster, Spell, MagicItem, God. Each owns a curated field projection matching its canonical `*Fields` shape — Monster/Spell lifted verbatim from the existing services (guaranteed parity), MagicItem/God newly written to `MagicItemFields`/`GodFields`. (The existing `FivetoolsXxxMapper.Map` uses a generic whole-entry `Clone()` that does NOT match these curated canonical shapes, so it is not reused for field projection; the shared win is the recall/diff/flag algorithm.)
- **BREAKING** — replace the four type-specific admin routes (`monster-recall`, `backfill-monsters`, `flag-unknown-monsters`, `backfill-spells`) with three type-parameterized routes: `GET entity-recall?type=`, `POST backfill-entities?type=`, `POST flag-unknown-entities?type=` (supported types: Monster, Spell, MagicItem, God).
- **Delete** `MonsterBackfillService` and `SpellBackfillService`; port their behavior and tests onto the generic engine + providers.
- **DMG coverage run**: re-extract → recall+backfill+flag-unknown for the 4 types → corpus validate (0 failures for dmg14). `dnd_entities` re-ingest is deferred to when the Ollama-backed stack is up.

## Capabilities

### New Capabilities

- `fivetools-entity-backfill`: type-generic 5etools recall/backfill/flag engine + per-type providers + type-parameterized admin endpoints, supporting Monster, Spell, MagicItem, and God.

### Modified Capabilities

- `fivetools-monster-backfill`: monster recall/backfill/flag behavior is now provided by the generic engine's Monster provider; the monster-specific routes are removed in favor of the type-parameterized routes.
- `fivetools-spell-backfill`: spell backfill behavior is now provided by the generic engine's Spell provider; the `backfill-spells` route is removed in favor of the type-parameterized route.
- `monster-precision-flagging`: the `flag-unknown-monsters` route is replaced by the type-parameterized `flag-unknown-entities?type=Monster` route (same otherSource/unknown split, same gap-only NeedsReview write).

## Impact

- **Code**: `Features/Ingestion/FivetoolsIngestion/` — new `EntityBackfillService` + `IFivetoolsBackfillProvider` + four providers; deletes `MonsterBackfillService`, `SpellBackfillService`. `Features/Admin/BooksAdminEndpoints.cs` — route replacement.
- **API contracts**: `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` updated in the same commit (route rename).
- **Data**: `books/canonical/dmg14.json` (+ sibling `dmg14.errors.json`/`declined.json`) regenerated and hand-reviewed; `dnd_entities` re-ingest deferred.
- **Tests**: monster/spell backfill test suites ported to the generic engine; new provider and engine unit tests.
- **No dependency changes.**
