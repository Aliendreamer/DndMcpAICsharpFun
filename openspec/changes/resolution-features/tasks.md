# Tasks — resolution-features (three new grounded CharacterResolutionService features)

All tasks complete. Shipped commits 0a6d45d, 124babe, c7f3b06 (+ fix wave f165edc, spec reconcile cf79cfc). Full suite 1679/1679 with Docker; whole-branch review APPROVE.

## 1. `class resources` feature
- [x] 1.1 `class resources` branch + `ResolveClassResourcesAsync` (projected class table, `RowIndex==Level-1`, resource columns only via `NonResourceColumns` exclusion, per-cell provenance; `"none"`/`needsReview`). (0a6d45d)
- [x] 1.2 Integration tests (real Postgres, real 5etools columns): Barbarian Rages → ok + provenance; Fighter → `"none"`; missing table → `needsReview`. RED-first. (0a6d45d)

## 2. `saving throws` feature
- [x] 2.1 `SavingThrowProficiencies` static PHB `class → (Ability, Ability)` map (all 12 classes). (124babe)
- [x] 2.2 `saving throws` branch + pure static `ResolveSavingThrows` (`Classes[0]`→`sheet.Class`, all six abilities, computed/null provenance, unknown → `needsReview`). (124babe, f165edc)
- [x] 2.3 Unit tests: proficient adds PB / non-proficient doesn't; multiclass proficiency from starting class only; unknown → `needsReview`. RED-first. (124babe)

## 3. `spell count` feature (branch-per-caster-type)
- [x] 3.1 `spell count` branch + `ResolveSpellCountAsync`, data-driven known/prepared/non-caster (full `mod+level` / half `mod+level/2` min 1). (c7f3b06, f165edc)
- [x] 3.2 Integration tests — one per branch (known Bard reads Spells Known + provenance; prepared-full Cleric; prepared-half Paladin + null provenance; non-caster Fighter → `"no spellcasting"` + `needsReview`). RED-first. (c7f3b06, f165edc)

## 4. Real-infra grounding
- [x] 4.1 Satisfied inherently: Tasks 1 & 3 integration tests seed the REAL 5etools-projected tables (`Rages`, `Spells Known`) and assert grounded value + `ok`. (0a6d45d, c7f3b06)

## 5. Gates
- [x] 5.1 build 0/0; FULL `dotnet test` 1679/1679 with Docker; `dotnet format` clean; diff confined to `Features/Resolution/*` + 3 test files; no `.http`/insomnia change. Whole-branch review APPROVE.
