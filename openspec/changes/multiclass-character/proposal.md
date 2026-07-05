## Why

Multiclassing is certain in real D&D and it is the deliberate **composition** stress test for the companion — it must *compose* rules across classes, not look one up. It applies to **any class combination**, caster or not: a Rogue 3 / Fighter 2 "rogue warrior" is just as much a multiclass character as a Wizard 5 / Cleric 3. Slice-1's `CharacterSheet.Level` is a single `int` and cannot represent Fighter 3 / Wizard 2; "what level am I" has three answers (total level for proficiency bonus, per-class level for a class feature). This slice makes the **per-class level model** and the deterministic multiclass rules real.

Spellcasting is the *hardest sub-part* — slots are **not summed**; you compute a combined caster level (full + ½ + ⅓; Warlock Pact Magic separate) then read one Multiclass Spellcaster table, exactly the arithmetic an LLM flubs — so it is the sharpest proof of the engine. But it is one layer on top of a model that serves all multiclass; **non-caster combos (Rogue/Fighter, Barbarian/Fighter) work fully without ever touching it.**

## What Changes

**Layer 1 — all multiclass (caster or not):**
- **Per-class level model**: `CharacterSheet.Classes: List<ClassLevel>{class,level,subclass}` becomes the source of truth; `Class`/`Subclass`/`Level`/`ProficiencyBonus` become derived (primary class + total level). Existing single-class `HeroSnapshot` JSON migrates *tolerantly* (back-fill a 1-element list on deserialize; no data-migration script).
- **Multiclass validity**: deterministic ability-score **prerequisites** to multiclass in/out, and the **reduced proficiency subsets** each class grants when multiclassed.

**Layer 2 — engages only when caster classes are present:**
- **Spellcasting composition**: deterministic combined-caster-level arithmetic + the **Multiclass Spellcaster** combined-level→slots table; Warlock Pact Magic tracked separately; **spell save DC / attack per caster class** (not one combined number).

**Resolution + surface:**
- `CharacterResolutionService` gains the **single-vs-multi fork**: `resolve_spell_slots`, `resolve_spell_save_dc` (per-class), `resolve_multiclass_validity` (works for non-caster combos too), each returning `ResolvedFact` + `ProvenanceRef`.
- MCP: `resolve_spell_slots` (multiclass-aware) + `check_multiclass` tools.
- **Reference data**: the Multiclass Spellcaster slot table is **seeded into Postgres `StructuredTables`** (provenance FK → cited answer, like breath-weapon); the caster-type classification + combined-level arithmetic stay as deterministic code.

## Capabilities

### New Capabilities
- `multiclass-character`: a per-class level model with tolerant sheet migration; deterministic multiclass **validity** (prerequisites + proficiency subsets) for **any** combination; deterministic **spellcasting composition** (combined caster level + Multiclass Spellcaster table + Warlock carve-out + per-class save DC) when casters are present; and the single-vs-multi resolution fork exposed via MCP tools with provenance.

## Impact

- **Domain**: `CharacterSheet` (`Classes` list, derived flat fields, `[OnDeserialized]` back-fill, `SetSingleClass` helper, `ClassLevel`); writers that set `Class`/`Level` updated.
- **Persistence**: `HeroSnapshot.CharacterSheet` JSON column reads old single-class rows tolerantly (no migration script).
- **Resolution**: `CharacterResolutionService` (+ deterministic `MulticlassRules`, `MulticlassSpellcasting` helpers).
- **MCP**: extended feature-dispatch tools.
- **Data**: Multiclass Spellcaster table + PHB multiclassing prose refs seeded into `StructuredTables`.
- **Out of scope** (later slices): Extra-Attack non-stacking, per-class feature composition (channel divinity / rage stacking), spells-known/prepared limits.
