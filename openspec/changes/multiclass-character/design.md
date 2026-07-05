## Context

Slice-1 shipped `CharacterSheet` (flat `Class`/`Subclass`/`Level int` + spellcasting fields + `ResolvedChoices`), a deterministic `CharacterResolutionService` (feature-dispatch → deterministic rule + Postgres `StructuredTables` + `ProvenanceRef`), and the `ResolvedFact`/`ResolvedComponent` shape. `CharacterSheet` persists as a JSON column on `HeroSnapshot`. This slice extends that model to multiclass characters of **any** combination, with spellcasting composition as the hardest sub-case. See design §J.

## Goals / Non-Goals

**Goals:**
- Represent per-class levels (Rogue 3 / Fighter 2) with total level + proficiency bonus derived; migrate existing single-class heroes without a data-migration script.
- Deterministic multiclass **validity** (prerequisites + proficiency subsets) for any combination, caster or not.
- Deterministic **spellcasting composition** (combined caster level → Multiclass Spellcaster table; Warlock separate; per-class save DC) when casters are present.
- A single-vs-multi resolution fork exposed via MCP tools, with provenance.

**Non-Goals:**
- Extra-Attack non-stacking, per-class feature composition (channel divinity / rage stacking), spells-known/prepared limits — later slices.

## Decisions

- **`Classes: List<ClassLevel>` is the source of truth; flat fields derived.** `Class`/`Subclass` = primary (`Classes[0]`); `Level` = Σ per-class levels; `ProficiencyBonus` from total level. Flat props become read-only getters. Writers that set them switch to a `SetSingleClass(class, subclass, level)` helper. *Alternative:* replace flat fields outright — rejected (ripples every reader + needs an explicit migration). *Alternative:* parallel authoritative-flat — rejected (§J's two-sources-of-truth drift).
- **Tolerant JSON migration, no script.** An `[OnDeserialized]` hook back-fills `Classes` from the legacy flat `Class`/`Subclass`/`Level` when `Classes` is empty. Old `HeroSnapshot` rows load as a 1-element list; new writes serialize `Classes`. Round-trip tested.
- **Validity is deterministic code, all combos.** `MulticlassRules`: a per-class ability-score prerequisite map (Fighter STR13∨DEX13, Rogue DEX13, Paladin STR13∧CHA13, Wizard INT13, …) and a per-class *multiclass* proficiency-subset map (the reduced grants). `CanMulticlassInto(class, sheet)` → allowed + reason; `MulticlassProficiencies(class)`. Non-caster combos are fully served here.
- **Spellcasting composition is deterministic code + a seeded table.** `MulticlassSpellcasting`: a hardcoded caster-type map (full: Bard/Cleric/Druid/Sorcerer/Wizard; half: Paladin/Ranger, Artificer ⌈lvl/2⌉; third: Eldritch Knight / Arcane Trickster subclasses); combined caster level = Σ full + ⌊half/2⌋ + ⌊third/3⌋; **Warlock Pact Magic excluded** (its own table). The combined-level→slots **Multiclass Spellcaster table is seeded into `StructuredTables`** so the answer carries a provenance FK (matches the breath-weapon pattern, §J "another relational table"). The arithmetic is the part §J says must be deterministic code, not an LLM/table lookup. *Alternative:* hardcode the slot table too — rejected for provenance consistency with slice-1.
- **Per-class spell save DC / attack.** Each caster class uses its own spellcasting ability, so `resolve_spell_save_dc` returns one `ResolvedComponent` per caster class — never a single combined DC.
- **Resolution forks on the sheet, not the query text.** `resolve_spell_slots` inspects `Classes`: 0–1 caster class → that class's table; ≥1 caster in a multiclass → combined-caster-level path; Warlock pact slots always a separate component. Keeps the LLM out of the arithmetic.

## Risks / Trade-offs

- **[Derived flat fields break setters]** → Mitigation: grep all writers of `Class`/`Subclass`/`Level`, route through `SetSingleClass`; compile + tests catch the rest (warnings-as-errors).
- **[Tolerant migration mis-parses an odd legacy row]** → Mitigation: `[OnDeserialized]` only back-fills when `Classes` is empty AND a flat `Class` is present; unit-test the round-trip and the empty-both case.
- **[Caster-type / prereq data errors]** → Mitigation: the maps are small and fully unit-tested against known 5e cases; the seeded slot table validated against the PHB Multiclass Spellcaster table.
- **[Artificer / subclass-caster edge cases]** → Mitigation: explicit test cases (Artificer ⌈lvl/2⌉ half-round-up; Eldritch Knight third-caster only at subclass levels).

## Migration Plan

1. Ship the model + rules + resolution; seed the Multiclass Spellcaster table.
2. Existing single-class `HeroSnapshot` rows load tolerantly (back-filled `Classes`); no downtime, no script. New multiclass heroes write `Classes`.
3. Rollback: the flat fields still read correctly for single-class heroes; reverting the model leaves single-class data intact (multiclass heroes would lose the extra class rows — none exist pre-feature).

## Open Questions

- None blocking. Whether to later ground the caster-type map against 5etools class data (vs the hardcoded map) is deferred.
