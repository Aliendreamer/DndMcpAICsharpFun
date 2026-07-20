## Context

`CharacterResolutionService` (`Features/Resolution/`) resolves character facts deterministically from Postgres (`StructuredTables`, `ChoiceSetRows`) and exposes them via the per-user `resolve_character_feature` MCP tool. It covers breath weapon (choice→table, wired in `project-tables-from-5etools`) and the computed casting facts (slots/save DC/attack). The just-shipped class-progression tables (`phb14.table.<class>`, columns `Level|Proficiency Bonus|Features`) already sit in `StructuredTables` but nothing reads them. Subclass granted spells live only in raw 5etools `additionalSpells` (`[{"prepared"|"known"|"expanded": {"<charLevel>": ["spell", …]}}]`), not in Postgres.

`LevelUpPlanner` (`Features/CharacterAdvice/`) also derives features-by-level, but from the class *entity's* `ClassFeatures` field in Qdrant via `ClassFeatureRefParser`, on the recommendation path — a different store and use case; the resolver grounds on the Postgres table instead, so there is no shared code to reuse or duplicate.

## Goals / Non-Goals

**Goals:**
- Resolve `"class features"` — the cumulative base-class features a character has (levels 1..current) + current proficiency bonus, per class, cited to the projected class table.
- Resolve `"subclass spells"` — the spells a spellcasting subclass grants at levels ≤ current, cited to a projected subclass-spells table.
- Reuse the shipped project → `StructuredFactProjector` → resolve pattern; expose both through the existing tool with zero new HTTP surface.

**Non-Goals:**
- Subclass *feature* descriptions or non-spell subclass features (RAG's job — this anchors the names/levels/spells).
- Sorcerer Draconic-Bloodline damage (prose-only, not structured).
- Any `CharacterSheet` schema change (`Subclass`/`Classes`/`Level` already exist).
- Full multiclass-subclass support beyond the sheet's single `Subclass` field (documented limitation).

## Decisions

**D1 — `"class features"` grounds on the projected class table (Postgres), cumulative.** For each `sheet.Classes[]` entry, resolve `phb14.table.<class-slug>` (via `EntityIdSlug`), read all rows with `level ≤ ClassLevel`, split each `Features` cell (comma-joined names) into the cumulative feature list, and take the current level's `Proficiency Bonus` cell. One `ResolvedComponent` per class (multiclass-aware, like `spell save dc`). A class whose table is absent (non-projected book) → `needsReview`, never fabricated. Cumulative (not just the current level's row) is chosen because "what features does my character have" is the useful grounded fact; the value string groups by level. Alternative rejected: reuse `LevelUpPlanner`/`ClassFeatureRefParser` — it reads Qdrant entities, not the resolver's Postgres store, and is scoped to a single level-up delta.

**D2 — `"subclass spells"` needs a projection first.** A new `SubclassSpellsProjector` (a console step composed by `ProjectTablesRunner`, exactly like `DraconicAncestryResolutionProjector`) reads each subclass's `additionalSpells` for a projected book and emits `CanonicalTable` `<slug>.table.<subclass-slug>-spells`, columns `["level","spells"]`, one row per grant level (`spells` = comma-joined). `StructuredFactProjector` lands it. The resolver then reads it from Postgres — same store as every other resolver. Alternative rejected: read raw 5etools live in the resolver — breaks the "resolve from Postgres" contract and couples the request path to the filesystem.

**D3 — Union the `additionalSpells` grant kinds.** `prepared` (always-prepared, Cleric/Paladin/etc.), `known` (added to known, Warlock patron pre-Tasha), and `expanded` (expanded list a Warlock *may* learn) are all "spells this subclass grants" for the resolver's purpose — union them per level. The level keys are CHARACTER levels. Unrecognized `additionalSpells` shapes (`_` choose-blocks, ability-keyed) are skipped-and-noted, not emitted wrong.

**D4 — Subclass lookup is source-agnostic.** `sheet.Subclass` is a bare name ("Life Domain") with no book. The resolver slugs it (`EntityIdSlug`-style) to `<subclass-slug>-spells` and queries `StructuredTables` for a `CanonicalId` ending in `.table.<subclass-slug>-spells` (a subclass name is unique enough across our books). Not found → `needsReview`.

**D5 — Grounding contract distinguishes absent from empty.** No table found → `needsReview` ("I can't ground this"). Table found but the subclass grants no spells (e.g. Champion Fighter) → Confidence `ok`, value "none" — an honest grounded negative, not a fabrication and not a failure.

## Risks / Trade-offs

- **Cumulative class-features value is verbose** → acceptable; it's the complete grounded fact and the component list stays per-class.
- **`additionalSpells` has shape variants** (`choose`/ability-keyed) beyond the three handled → skip-and-note in the projector; unit-test `prepared` (Life Domain) + `expanded`/`known` (a Warlock patron) so the common paths are proven; a genuinely unhandled subclass simply has no spells table → resolver `needsReview`, never wrong.
- **Single `Subclass` field limits multiclass-subclass** → document; resolve the sheet's one `Subclass`. A future sheet change could carry per-class subclasses.
- **Class-table Features cell is names only** → intended; descriptions come from RAG. The resolver's job is the deterministic *which + when*.

## Migration Plan

1. Land the two resolvers + `SubclassSpellsProjector` + console wiring (unit-green).
2. `ProjectTables --all` → adds subclass-spells tables to projected canonicals; review the diff; confirm reload + class tables unchanged.
3. Real-Postgres integration test proves both resolvers end-to-end on real PHB data.
4. Rollback = `git checkout books/canonical/*.json` + revert code; no schema/DB migration.

## Open Questions

- None blocking. Which non-PHB books carry subclass `additionalSpells` (XGE reprints, etc.) is discovered by the `--all` run; unhandled shapes are skipped-and-noted, deferred if any surface.
