## ADDED Requirements

### Requirement: Resolve class resources by level from the projected class tables

`CharacterResolutionService` SHALL resolve a `class resources` feature. For each class on the sheet it SHALL read the projected class progression table (canonical id ending `.table.<class-slug>`) at the row for the class's level and emit the class-specific resource columns — every column except `Level`, `Proficiency Bonus`, `Features`, `Cantrips Known`, `Spells Known`, `Spell Slots`, `Slot Level`, and the spell-level columns `1st`–`9th` (case-insensitive). Each non-empty resource cell SHALL be a component carrying the cell's provenance. A class whose table defines no resource column SHALL resolve to `"none"` (confidence `ok`); a missing or ambiguous table SHALL resolve to `needsReview`. The engine SHALL NOT fabricate a resource value.

#### Scenario: Barbarian rages resolve at level

- **WHEN** a level-5 Barbarian's `class resources` is resolved and the projected `.table.barbarian` has a `Rages` column
- **THEN** the result includes a component `"Barbarian Rages"` with the value from the level-5 row and that cell's provenance, confidence `ok`

#### Scenario: A class with no resource column resolves to none, not fabricated

- **WHEN** a Fighter's `class resources` is resolved and the `.table.fighter` has only `Level`, `Proficiency Bonus`, `Features`
- **THEN** the Fighter component resolves to `"none"` (no invented number)

#### Scenario: Missing class table is needsReview

- **WHEN** a class has no matching `.table.<class>` in `StructuredTables`
- **THEN** that class resolves to `needsReview`, not a fabricated value

### Requirement: Resolve per-ability saving-throw bonuses

`CharacterResolutionService` SHALL resolve a `saving throws` feature. Using a static map of each class to its two proficient save abilities, the proficient set SHALL be taken from the starting class (`Classes[0]`) only. For all six abilities it SHALL emit a component whose value is `Modifier(score) + (proficient ? proficiency bonus : 0)`, indicating proficiency. These values are computed and SHALL carry no provenance. If the starting class is not in the map, the fact SHALL resolve to `needsReview`.

#### Scenario: Proficient saves add the proficiency bonus

- **WHEN** a level-4 Wizard (INT/WIS saves, proficiency bonus +2) has WIS 14
- **THEN** the Wisdom component is `+4` and marked proficient, while a non-proficient save (e.g. Strength) is just its ability modifier with no proficiency bonus

#### Scenario: Save proficiency comes only from the starting class

- **WHEN** a Fighter 1 / Wizard 1 character (Classes[0] = Fighter, saves STR/CON) resolves `saving throws`
- **THEN** the proficient saves are Fighter's (STR, CON) only — Wizard's INT/WIS are not added

#### Scenario: Unknown class is needsReview

- **WHEN** the starting class is not present in the class→saves map (homebrew)
- **THEN** the fact resolves to `needsReview` rather than guessing the proficient saves

### Requirement: Resolve prepared/known spell counts per caster type

`CharacterResolutionService` SHALL resolve a `spell count` feature, classifying each class by its data (not by slot source). A class whose projected table has a `Spells Known` column is a known caster and SHALL read that cell at the class level (carrying its provenance). A class with no `Spells Known` column but a non-null spellcasting ability is a prepared caster and SHALL compute the count from its ability modifier plus level (full casters — Cleric/Druid/Wizard: `mod + level`; half casters — Paladin: `mod + level/2`; both minimum 1), carrying no provenance. A class with no spellcasting ability is a non-caster and SHALL contribute `"no spellcasting"` with no fabricated number. Where a `Cantrips Known` column exists, the cantrip count SHALL be added from that cell. Classification is by the presence of the `Spells Known` cell (data-driven), so a class with no `Spells Known` cell but a spellcasting ability is treated as prepared even if its table is absent (accepted — the corpus carries every class table). A fact where no class contributes a spell count (e.g. all classes are non-casters) SHALL resolve to `needsReview`.

#### Scenario: Known caster reads Spells Known from the table

- **WHEN** a level-3 Bard's `spell count` is resolved and `.table.bard` has a `Spells Known` column
- **THEN** the Bard component's known-spell count is the level-3 `Spells Known` cell value with that cell's provenance

#### Scenario: Prepared caster computes from ability and level

- **WHEN** a level-5 Cleric with Wisdom 16 (`+3`) is resolved
- **THEN** the prepared count is `mod + level = 3 + 5 = 8`, carrying no provenance (computed)

#### Scenario: Paladin uses the half-level formula

- **WHEN** a level-6 Paladin with Charisma 14 (`+2`) is resolved
- **THEN** the prepared count is `mod + level/2 = 2 + 3 = 5`, minimum 1

#### Scenario: Non-caster contributes no fabricated count

- **WHEN** a Fighter (no spellcasting) is resolved
- **THEN** its contribution is `"no spellcasting"`, never a numeric count
