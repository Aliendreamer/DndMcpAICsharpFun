## ADDED Requirements

### Requirement: Project subclass additional spells into canonical tables

The console SHALL, for each spellcasting subclass of a projected book, project the subclass's 5etools `additionalSpells` grants into a `CanonicalTable` with id `<slug>.table.<subclass-slug>-spells`, columns `["level","spells"]`, one row per grant-level (`spells` = the comma-joined spells granted at that level, unioning the `prepared`/`known`/`expanded` grant kinds). Unrecognized `additionalSpells` shapes SHALL be skipped without emitting a wrong table.

#### Scenario: Life Domain spells projected

- **WHEN** the console projects PHB
- **THEN** `phb14.table.life-domain-spells` exists with columns `["level","spells"]` and a row for level 1 containing `bless` and `cure wounds`

#### Scenario: Warlock patron expanded spells projected

- **WHEN** the console projects a Warlock patron subclass whose `additionalSpells` uses the `expanded` grant kind
- **THEN** its `<slug>.table.<subclass-slug>-spells` table lists the expanded spells per grant-level

#### Scenario: Subclass with no additional spells produces no spells table

- **WHEN** a subclass has no `additionalSpells` (e.g. a martial subclass)
- **THEN** no `<subclass-slug>-spells` table is emitted for it
