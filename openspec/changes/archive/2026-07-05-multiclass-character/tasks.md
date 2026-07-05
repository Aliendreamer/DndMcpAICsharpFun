## 1. Per-class level model (Layer 1 — all multiclass)

- [ ] 1.1 Add `ClassLevel { string Class; int Level; string Subclass; }` and `CharacterSheet.Classes: List<ClassLevel>` (persisted source of truth)
- [ ] 1.2 (test) Derived accessors: `Level` = Σ per-class levels, `Class`/`Subclass` = primary (Classes[0]), `ProficiencyBonus` from total level — for Rogue 3 / Fighter 2 → total 5
- [ ] 1.3 Make `Class`/`Subclass`/`Level` read-only derived getters; add `SetSingleClass(class, subclass, level)` helper; grep + route all writers of the flat fields through it
- [ ] 1.4 (test) Tolerant migration: legacy JSON (flat `Class`/`Level`, no `Classes`) `[OnDeserialized]` back-fills a 1-element list; round-trip preserves the character; empty-both case is safe
- [ ] 1.5 Confirm `HeroSnapshot` JSON serialization writes `Classes` and reads legacy rows (no migration script); build 0/0

## 2. Multiclass validity (Layer 1 — all combos, caster or not)

- [ ] 2.1 (test) Prerequisites: `MulticlassRules.CanMulticlassInto(class, sheet)` — Rogue needs DEX13, Fighter STR13∨DEX13, Paladin STR13∧CHA13, Wizard INT13, … (allowed + failed-prereq reason)
- [ ] 2.2 Implement the per-class ability-score prerequisite map + `CanMulticlassInto`/`CanMulticlassOutOf`
- [ ] 2.3 (test) Proficiency subsets: `MulticlassProficiencies(class)` returns the reduced grant (Fighter → light/medium armor, shields, martial weapons; NOT heavy armor / saving throws)
- [ ] 2.4 Implement the per-class multiclass-proficiency-subset map

## 3. Spellcasting composition (Layer 2 — casters only)

- [ ] 3.1 (test) Caster-type map + combined caster level: full (Bard/Cleric/Druid/Sorcerer/Wizard) + ⌊half/2⌋ (Paladin/Ranger; Artificer ⌈lvl/2⌉) + ⌊third/3⌋ (Eldritch Knight / Arcane Trickster); Warlock EXCLUDED. Cases: Wizard5/Cleric3→8; Paladin6/Sorcerer2→5; Artificer rounding
- [ ] 3.2 Implement `MulticlassSpellcasting.CombinedCasterLevel(classes)` + caster-type classification
- [ ] 3.3 Seed the Multiclass Spellcaster combined-level→slots table into `StructuredTables` (with a PHB multiclassing provenance ref); a small seeding step like the breath-weapon table
- [ ] 3.4 (test) Warlock Pact Magic computed separately (level → pact slot count + slot level), never merged into the combined pool

## 4. Resolution fork + MCP surface

- [ ] 4.1 (test) `resolve_spell_slots` forks on `Classes`: single caster → direct; multiclass casters → combined-level → Multiclass Spellcaster table; Warlock pact as a separate component; result cites the seeded table
- [ ] 4.2 Implement the fork in `CharacterResolutionService` returning `ResolvedFact` + `ResolvedComponent`s + `ProvenanceRef`
- [ ] 4.3 (test) `resolve_spell_save_dc` returns one component per caster class (Cleric3/Wizard2 → WIS-based + INT-based)
- [ ] 4.4 Implement `resolve_spell_save_dc` (per-class) and `resolve_multiclass_validity` (prereq + proficiency subset; works for non-caster combos)
- [ ] 4.5 Expose `resolve_spell_slots` (multiclass-aware) + `check_multiclass` MCP tools; wire into the feature-dispatch; update `.http` + `.insomnia` if any HTTP surface changes

## 5. Validation

- [ ] 5.1 `dotnet build` 0/0 (warnings-as-errors); full non-persistence suite green; persistence tests (HeroSnapshot round-trip) green
- [ ] 5.2 Live check: resolve slots for a multiclass caster (Paladin6/Sorcerer2), a Warlock/Sorcerer, a single-class Wizard, and validity for a non-caster Rogue/Fighter — confirm correct arithmetic + provenance + the non-caster path never touches spellcasting
- [ ] 5.3 Update docs where character/resolution config is documented; `.http`/`.insomnia` synced for any new/changed route
