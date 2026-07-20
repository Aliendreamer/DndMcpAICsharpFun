## ADDED Requirements

### Requirement: Project captioned 5etools tables into canonical

The system SHALL project every captioned `{type:table}` block found in a book's 5etools entity entries (races, classes, feats, backgrounds, items, optionalfeatures) whose `source` equals the book's `fivetoolsSourceKey` into a `CanonicalTable`, preserving the caption as the table name, `colLabels` as columns, and rows as cell values.

#### Scenario: Draconic Ancestry projected with a sluggable id

- **WHEN** the console projects tables for PHB (`fivetoolsSourceKey=PHB`, slug `phb14`)
- **THEN** the canonical `tables[]` contains a table with id `phb14.table.draconic-ancestry`, name `Draconic Ancestry`, columns `["Dragon","Damage Type","Breath Weapon"]`, and a row `["Black","Acid","5 by 30 ft. line (Dex. save)"]`

#### Scenario: Uncaptioned embedded tables are skipped

- **WHEN** an entity contains a `{type:table}` block with no `caption`
- **THEN** that block SHALL NOT be projected (no stable id can be derived)

### Requirement: Synthesize class progression tables

The system SHALL emit one progression `CanonicalTable` per class of the book, keyed by character level 1–20, with id `<book-slug>.table.<class-slug>`. For a class with no `classTableGroups` the columns SHALL be `Level`, `Proficiency Bonus`, and `Features` (proficiency bonus from the standard curve `2 + (level-1)/4`; features per level from `classFeatures`). For a class with `classTableGroups` the group columns SHALL be appended, with `{@filter …}`/`{@tag …}` markup stripped to plain labels and `rowsSpellProgression` expanded into per-level spell-slot cells joined on level.

#### Scenario: Martial class table synthesized

- **WHEN** projecting the Fighter class (no `classTableGroups`)
- **THEN** `phb14.table.fighter` has columns `["Level","Proficiency Bonus","Features"]`, 20 rows, and level 5 shows Proficiency Bonus `+3`

#### Scenario: Caster class spell slots expanded

- **WHEN** projecting the Wizard class (has `classTableGroups` with `rowsSpellProgression`)
- **THEN** `phb14.table.wizard` includes per-level spell-slot columns with plain labels (no `{@filter …}` markup) and the 1st-level slot count filled per level

### Requirement: Replace canonical tables for official books only

The console SHALL rebuild and REPLACE the canonical `tables[]` wholesale for a book that has a `fivetoolsSourceKey`, discarding any MinerU-extracted tables, and SHALL skip (leave untouched) any book without a resolvable source key.

#### Scenario: Official book tables replaced

- **WHEN** the console runs for an official book whose canonical previously held MinerU tables
- **THEN** the written `tables[]` contains only tables tagged `dataSource:"5etools-table-projection"` and no MinerU-origin tables remain

#### Scenario: Homebrew book skipped

- **WHEN** the console targets a book with no `fivetoolsSourceKey`
- **THEN** the console SHALL skip it, leave its canonical `tables[]` unchanged, and report that it was skipped

### Requirement: Written canonical is uniquely-id'd and ingestable

The system SHALL guarantee the rewritten canonical has unique table ids and reloads cleanly. On a slug collision the console SHALL disambiguate with a numeric suffix (`-2`, `-3`, …), and after writing SHALL reload the file through `CanonicalJsonLoader` without error.

#### Scenario: Colliding captions disambiguated

- **WHEN** two projected tables would slug to the same id
- **THEN** the second SHALL receive a `-2` suffix so all table ids are unique

#### Scenario: Round-trip load succeeds

- **WHEN** the console finishes writing a book's canonical
- **THEN** `CanonicalJsonLoader` SHALL load the file without throwing a duplicate-id or schema error

### Requirement: Wire Draconic Ancestry into the breath-weapon resolver

The console SHALL, for PHB, additionally emit resolution artifacts that drive `CharacterResolutionService` breath-weapon resolution: a NORMALIZED `phb14.table.draconic-ancestry` (columns `ancestry`, `damageType`, `breathArea`, `saveAbility`), a `phb14.choiceset.draconic-ancestry` mapping each ancestry to that table's row, and a `phb14.table.breath-damage-by-tier` (columns `tier`, `dice`). The generic captioned projection SHALL cede the `phb14.table.draconic-ancestry` id to this normalized table (resolution-owned id).

#### Scenario: Normalized draconic table produced

- **WHEN** the console projects PHB
- **THEN** `phb14.table.draconic-ancestry` has columns `["ancestry","damageType","breathArea","saveAbility"]` and the Black row is `["Black","acid","5 by 30 ft. line","Dexterity"]` (breath area and save split from the 5etools "5 by 30 ft. line (Dex. save)" cell)

#### Scenario: Companion choiceset and tier table produced

- **WHEN** the console projects PHB
- **THEN** `phb14.choiceset.draconic-ancestry` has one option per ancestry pointing at `phb14.table.draconic-ancestry` with the matching rowIndex, and `phb14.table.breath-damage-by-tier` has columns `["tier","dice"]` with rows `[1,1d10],[2,2d6],[3,3d6],[4,4d6]`

#### Scenario: Breath weapon resolves end-to-end

- **WHEN** the projected PHB artifacts are landed via `StructuredFactProjector` and a level-5 Black Dragonborn's `ancestry` choice (`phb14.choiceset.draconic-ancestry:Black`) is resolved
- **THEN** `CharacterResolutionService` breath-weapon resolution returns a value describing `"5 by 30 ft. line of acid, Dexterity save DC <n>, 1d10"` with confidence `"ok"` (not `needsReview`)
