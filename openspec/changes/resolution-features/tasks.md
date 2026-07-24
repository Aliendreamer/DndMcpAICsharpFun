# Tasks — resolution-features (three new grounded CharacterResolutionService features)

## 1. `class resources` feature
- [ ] 1.1 Add a `class resources` branch to `ResolveForSheetAsync` + a `ResolveClassResourcesAsync` method. Per class, look up the projected class table (`.EndsWith($".table.{EntityIdSlug.Slug(c.Class)}")`, Take(2) ambiguity guard — mirror `ResolveClassFeaturesAsync`), read the row at `RowIndex == c.Level - 1`, and emit every column EXCEPT the structural/spellcasting set `{Level, Proficiency Bonus, Features, Cantrips Known, Spells Known, Spell Slots, Slot Level, 1st..9th}` (case-insensitive) as a component `"<Class> <Column>"` with the cell provenance, non-empty cells only. No resource column → `"none"` (`ok`); missing/ambiguous table → `needsReview`.
- [ ] 1.2 Unit tests: Barbarian Rages resolves at level (fake table with a `Rages` column) → component + `ok`; Fighter (only Level/Proficiency Bonus/Features) → `"none"`, no fabricated value; missing table → `needsReview`. RED-first.

## 2. `saving throws` feature
- [ ] 2.1 Add a static PHB `class → (Ability, Ability)` save map (all 12 classes) — a new small static helper (or alongside `MulticlassSpellcasting`), returning the two proficient save abilities or null for an unknown class. Values: Barbarian STR/CON, Bard DEX/CHA, Cleric WIS/CHA, Druid INT/WIS, Fighter STR/CON, Monk STR/DEX, Paladin WIS/CHA, Ranger STR/DEX, Rogue DEX/INT, Sorcerer CON/CHA, Warlock WIS/CHA, Wizard INT/WIS.
- [ ] 2.2 Add a `saving throws` branch + `ResolveSavingThrows` (pure/computed — no DB). Proficient set from `Classes[0]` only. Emit all six abilities: `Modifier(score) + (proficient ? ProficiencyBonus : 0)`, mark proficiency in the value; computed → null provenance. Starting class not in the map → `needsReview`.
- [ ] 2.3 Unit tests: proficient save adds PB, non-proficient does not; multiclass save proficiency comes only from `Classes[0]`; unknown starting class → `needsReview`. RED-first.

## 3. `spell count` feature (branch-per-caster-type)
- [ ] 3.1 Add a `spell count` branch + `ResolveSpellCountAsync`. Classify each class by DATA (not slot-source): KNOWN ⇔ the class table row has a `Spells Known` cell → read it (cell provenance) [Bard/Sorcerer/Ranger/Warlock]; PREPARED ⇔ no `Spells Known` column but `SpellcastingAbility(class) != null` → compute via `SpellcastingAbility`, full-caster `mod + level` / half-caster (`Classify` is half — Paladin) `mod + level/2`, both min 1 (computed, null provenance); NON-caster ⇔ `SpellcastingAbility == null` → `"no spellcasting"`. Add `Cantrips Known` cell where the column exists. Known caster with missing table, or no class contributes → `needsReview`.
- [ ] 3.2 Unit tests — ONE PER BRANCH (the classifier-branch discipline): known caster reads `Spells Known` from the table (cell provenance); prepared full-caster `mod + level`; Paladin half `mod + level/2` min 1; non-caster → `"no spellcasting"`, no count; a warlock known-count branch. RED-first each.

## 4. Real-infra grounding
- [ ] 4.1 A real-Postgres integration test (reuse `PostgresFixture`) seeding a class progression table with the REAL 5etools column names (`Rages`; `Spells Known`) and asserting `ResolveClassResourcesAsync` + the `spell count` known-caster path return the seeded value with `Confidence=="ok"` — proves the filter/read matches real column names, not just fakes.

## 5. Gates
- [ ] 5.1 `dotnet build` 0/0; FULL `dotnet test` green (Docker for the integration test; if down, run non-persistence + note); `dotnet format` clean on touched files; `git diff --stat` confined to `Features/Resolution/*` + tests; no `.http`/insomnia change.
