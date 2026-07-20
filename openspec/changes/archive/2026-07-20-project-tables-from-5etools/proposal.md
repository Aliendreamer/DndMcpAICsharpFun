## Why

MinerU-extracted tables in the official-book canonicals are 45% degenerate (599/1325): the dominant failure is not stat-block noise but **parser column-collapse** — real class-progression, spell-slot, wild-magic, and armor tables flattened into single-column or ragged forms. Naively filtering them (the deferred `filter-degenerate-tables`) would delete the most valuable table content. For every official/5etools book, the same tables already exist as clean, structured JSON in 5etools — correct columns, correct rows — so we can project them deterministically instead of rescuing mangled OCR output.

## What Changes

- Add a `Tools/ProjectTables` console (build-time canonical authoring; no runtime endpoint/auth surface) that, for a book with a `fivetoolsSourceKey`, rebuilds the canonical `tables[]` from 5etools and **replaces** the MinerU tables wholesale. Invoked per book slug or `--all`; homebrew books (no source key) are skipped.
- Project **captioned embedded `{type:table}` blocks** (e.g. Draconic Ancestry, Runes Known) from the book's 5etools entity files into `CanonicalTable`, with sluggable ids (`phb14.table.draconic-ancestry`).
- Project **class progression tables** into `CanonicalTable`: synthesize martial-class tables (Level / Proficiency Bonus / Features) from `classFeatures` + the standard proficiency curve; for caster classes, parse `classTableGroups` (strip `{@filter …}` markup, expand `rowsSpellProgression`) and merge spell-slot columns keyed by level. Ids `<slug>.table.<class-slug>`.
- Tag projected tables `dataSource:"5etools-table-projection"` for audit; enforce a unique-id invariant + load round-trip on the rewritten canonical.
- **BREAKING (data):** official-book canonical `tables[]` are replaced — the previously committed MinerU tables for these books are discarded in favor of the 5etools projection.

## Capabilities

### New Capabilities
- `fivetools-table-projection`: Deterministic projection of 5etools structured tables (captioned embedded `{type:table}` blocks and synthesized class-progression tables) into official-book canonical `tables[]`, with correct sluggable ids, replacing MinerU-extracted tables for books that have a `fivetoolsSourceKey`.

### Modified Capabilities
<!-- No existing spec's REQUIREMENTS change; downstream (structured-knowledge-store / character-fact-resolution) consumes CanonicalTable unchanged. -->

## Impact

- **New:** `Tools/ProjectTables/` console; reuses `CanonicalJsonLoader`, `EntityIdSlug`, and the 5etools data under `5etools/` (races/classes/feats/backgrounds/items/optionalfeatures). No DI/DB needed by the console itself.
- **Data:** rewrites `books/canonical/<slug>.json` `tables[]` for all official books (PHB, DMG, XGE, MPMM, MTF, ERLW, SCAG, MM, TCE) — reviewed in the canonical diff.
- **Downstream (unchanged code path):** `ingest-entities` → `StructuredFactProjector` → Postgres `StructuredTables`/`Rows` → `CharacterResolutionService`, which now resolves against correctly-id'd tables (e.g. breath-weapon via `phb14.table.draconic-ancestry`).
- **Deferred, unaffected:** `filter-degenerate-tables` (MinerU homebrew junk-filter) and `table-name-from-heading` remain deferred; they apply only when a non-5etools/homebrew book is added.
