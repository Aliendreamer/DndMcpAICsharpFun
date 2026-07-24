## Why

`CharacterResolutionService` resolves character features deterministically from structured facts in Postgres (with provenance + an `ok`/`needsReview` confidence). It covers breath weapon, spell slots, spell save DC/attack, class features, and subclass spells — but several everyday character facts a player or DM asks for are still missing, even though the data to ground them already exists. The 5etools-projected class progression tables (in `StructuredTables`) carry per-level resource columns (Rages, Ki Points, Sorcery Points, Sneak Attack, Invocations Known, Spells Known…) that no resolver reads today; and saving-throw / spell-count facts are pure PHB rules the engine can compute deterministically. This is Spec A of a two-spec split — Armor Class is deferred to a later `structured-armor-and-ac` change because `CharacterSheet` stores `Equipment` as free-text with no structured worn-armor field, so AC cannot be grounded yet.

## What Changes

- Add three new grounded features to `CharacterResolutionService`, each following the existing pattern (a branch in `ResolveForSheetAsync` + a `Resolve…Async` method returning `ResolvedFact` with per-component provenance and an `ok`/`needsReview` confidence). No fabrication — absent grounding → `needsReview`.
- **`class resources`** *(table-grounded)*: per class, read the projected class progression table (`.table.<class>`) row at the character's level and emit the class-specific resource columns (everything except the structural/spellcasting set). Fighter (no resource column) → honest `"none"`; missing table → `needsReview`. Provenance from each cell.
- **`saving throws`** *(static-map + computed)*: a static `class → [two save abilities]` map (PHB, mirroring the existing `SpellcastingAbility` map) drives, for all six abilities, `mod + (proficient ? PB : 0)`; proficient set = the starting class (`Classes[0]`) only (5e grants no save proficiencies from multiclassing). Computed → null provenance. Class not in the map → `needsReview`.
- **`spell count`** *(hybrid, branch-per-caster-type)*: per class, branch on caster type — known casters read the `Spells Known` cell at level (table-grounded); prepared casters compute `mod + level` (Cleric/Druid/Wizard) or `mod + level/2` min 1 (Paladin) via `SpellcastingAbility`; non-casters → `"no spellcasting"`. Cantrips from the `Cantrips Known` cell where present.

## Capabilities

### New Capabilities

- `resolution-features`: `CharacterResolutionService` additionally resolves `class resources` (per-level class resource counts from the projected class tables), `saving throws` (per-ability save bonuses from a static class→saves map + computed modifiers), and `spell count` (prepared/known spell counts, table-grounded for known casters and formula-based for prepared casters) — each grounded, cited where cell-sourced, and `needsReview` rather than fabricated when grounding is absent.

### Modified Capabilities

<!-- Extends the resolution engine additively; no existing spec's REQUIREMENTS change. -->

## Impact

- `Features/Resolution/CharacterResolutionService.cs` — three new feature branches + resolver methods.
- A new static `class → [save abilities]` map (new small helper or alongside `MulticlassSpellcasting`).
- Reuses the already-projected `StructuredTables` (class progression tables) + `MulticlassSpellcasting.SpellcastingAbility`/caster-type helpers + `CharacterSheet` ability scores and `ProficiencyBonus`.
- Tests: per-branch unit tests (esp. `spell count`'s caster-type branches) + a real-Postgres integration test (reuse `PostgresFixture`) for the table-reading paths.
- **No HTTP endpoint change**, no schema change, no `.http`/insomnia change. Armor Class explicitly out of scope (deferred to `structured-armor-and-ac`).
