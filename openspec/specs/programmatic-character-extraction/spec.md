# programmatic-character-extraction Specification

## Purpose
TBD - created by archiving change programmatic-extraction-phase2a. Update Purpose after archive.
## Requirements
### Requirement: Class parsing

The extractor SHALL produce one Class entity per class defined in a book, anchored on the "Hit Dice:" line under its
"Class Features" heading and named only from the class's chapter heading. It SHALL fill `ClassFields`: `hd`,
saving-throw `proficiency`, `startingProficiencies`, `multiclassing` when printed, `subclassTitle` and `entries`.
`classFeatures` SHALL hold one ref per feature in the 5etools convention: `Name|Class|ClassSource|Level`, with
`ClassSource` empty for PHB, and the subclass-gaining features as `{"classFeature": ref, "gainSubclassFeature": true}`.

#### Scenario: PHB barbarian

- **WHEN** the PHB is extracted
- **THEN** `phb14.class.barbarian` has `hd` 1d12, saving throws Strength and Constitution, `subclassTitle` "Primal
  Path", and a `Rage|Barbarian||1` feature ref

#### Scenario: Coach can read the refs

- **WHEN** every `classFeatures` ref in the new `phb14.json` is parsed by the coach's feature-ref parser
- **THEN** none fails to parse

### Requirement: Class level table

The extractor SHALL read a class's level table into rows and columns from OCR line boxes, with digit repair per cell,
and use it for feature levels and the class checks. A table that cannot be read completely SHALL leave the class
NeedsReview with whatever was read, not drop the class.

#### Scenario: Twenty rows

- **WHEN** the Fighter table is read
- **THEN** it has 20 rows, levels 1–20, with proficiency bonus +2 at level 1 and +6 at level 20

#### Scenario: Unreadable table

- **WHEN** a table's Features column cannot be aligned with its rows
- **THEN** the class is still produced, its `level-table-read` check fails with a reason naming the class, and the
  entity is NeedsReview

### Requirement: Subclass parsing

The extractor SHALL produce one Subclass entity per subclass found under a class's subclass-group heading, in the
class's own book or in a supplement. It SHALL fill `className`, `classSource` (the 5etools key of the book defining
the parent class), `shortName`, `entries` and 6-part `subclassFeatures` refs
(`Name|Class|ClassSource|ShortName|SubclassSource|Level`, `ClassSource` empty for PHB) with the level of each feature.

#### Scenario: Supplement subclass of a PHB class

- **WHEN** XGE is extracted
- **THEN** `xge.subclass.path-of-the-zealot` has `className` "Barbarian", `classSource` "PHB", and features at levels
  3, 6, 10 and 14

### Requirement: Race and subrace parsing

The extractor SHALL produce a Race entity for each race whose traits ("Ability Score Increase.", "Size.", "Speed.")
are printed, named from its "<Race> Traits" heading minus "Traits". Each subrace heading inside the race section
followed by its own traits SHALL become a Subrace entity linked by `raceName` and `raceSource`. Size, speed,
darkvision, ability increases and the proficiencies SHALL be parsed into `RaceFields` and `SubraceFields`.

#### Scenario: Dwarf and hill dwarf

- **WHEN** the PHB is extracted
- **THEN** `phb14.race.dwarf` has size Medium, walking speed 25, darkvision 60 and +2 Constitution, and
  `phb14.subrace.dwarf-hill-dwarf` has `raceName` "Dwarf" and +1 Wisdom

### Requirement: Feat parsing

The extractor SHALL produce a Feat entity for each heading inside a feats section, with `prerequisites` from an
optional "Prerequisite:" line, `description` as the full text, and `grants` as the bullet benefits.

#### Scenario: Feat with a prerequisite

- **WHEN** the PHB is extracted
- **THEN** `phb14.feat.heavily-armored` has the prerequisite "Proficiency with medium armor" and grants that include
  +1 Strength

### Requirement: Background parsing

The extractor SHALL produce a Background entity for each "Skill Proficiencies:" anchor, named from the heading above,
filling skill and tool proficiencies, languages, equipment, and the feature's name and summary.

#### Scenario: Acolyte

- **WHEN** the PHB is extracted
- **THEN** `phb14.background.acolyte` has skills Insight and Religion and the feature "Shelter of the Faithful"

### Requirement: Character-data self-checks

Each of the six types SHALL carry named checks with human-readable reasons naming the values involved, feeding the
existing D4 disposition. The checks are:

- **Class:**
  - `hit-die-consistent`: the die agrees across "Hit Dice", "Hit Points at 1st Level" and "Hit Points at Higher
    Levels";
  - `level-table-read`;
  - `proficiency-bonus-steps`: +2 at levels 1–4, +3 at 5–8, +4 at 9–12, +5 at 13–16, +6 at 17–20;
  - `features-match-table`: every Features-column name has a feature heading in the text, and the reverse.
- **Subclass:**
  - `parent-class-known`;
  - `feature-levels-match-class`: the subclass's feature levels equal the class's subclass-feature levels.
- **Race and Subrace:**
  - `ability-increase-parsed`;
  - `size-valid`;
  - `speed-valid`: a multiple of 5;
  - for a subrace, `parent-race-known`.
- **Feat:**
  - `prerequisite-parsed`, when one is printed.
- **Background:**
  - `skills-valid`: every skill is one of the 18 skill names;
  - `feature-present`.

#### Scenario: Hit die disagreement

- **WHEN** a class's "Hit Dice" line reads 1d10 but "Hit Points at 1st Level" reads "12 + your Constitution modifier"
- **THEN** `hit-die-consistent` fails with a reason naming 1d10 and 12, and the class is NeedsReview

#### Scenario: Unknown skill

- **WHEN** a background's skill line OCRs as "Insigth, Religion"
- **THEN** `skills-valid` fails naming "Insigth", and the background is NeedsReview

