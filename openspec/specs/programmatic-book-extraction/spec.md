# programmatic-book-extraction Specification

## Purpose
TBD - created by archiving change programmatic-extraction-phase1. Update Purpose after archive.

## Requirements

### Requirement: Deterministic extractor service

The system SHALL provide a book extractor service, separate from the app container, that reads a book PDF and produces
Monster, Spell, MagicItem, Class, Subclass, Race, Subrace, Feat, Background, Weapon, Armor, Item, VehicleMount, Object,
Trap, DiseasePoison, Condition, God, Rule, Plane, Faction, Location and Lore entities using rules only. It MUST NOT
call any LLM and MUST NOT use a GPU. It SHALL read PDFs from a read-only mount of `books/`.

#### Scenario: Extraction without a model

- **WHEN** the extractor processes a book PDF
- **THEN** it produces entities without any network call to Ollama or another LLM service

#### Scenario: Read-only books

- **WHEN** the extractor runs
- **THEN** it cannot modify any file under `books/`

#### Scenario: Character types in one file

- **WHEN** the PHB is extracted
- **THEN** `phb14.json` holds its classes, subclasses, races, subraces, feats and backgrounds alongside its spells

#### Scenario: Equipment types in one file

- **WHEN** the PHB is extracted
- **THEN** `phb14.json` also holds its weapons, armor, adventuring gear and conditions

#### Scenario: World types in one file

- **WHEN** the PHB is extracted
- **THEN** `phb14.json` also holds its gods and named rules

### Requirement: Page reader

The reader SHALL OCR each page rendered at 200 dpi with RapidOCR (PP-OCRv5 English recognition, CPU), or use the PDF
text layer when that page's text passes the measured quality threshold. It SHALL rebuild reading order (two columns,
same-row fragments merged in x order, headers and footers removed, hyphenated line ends joined) and repair digit noise
(S→5, O→0, l/I→1, Z→2, B→8) only inside tokens that are numeric or dice expressions. Per-page OCR results SHALL be
cached so an interrupted run resumes without re-reading finished pages.

#### Scenario: Digit noise in dice

- **WHEN** a hit-points line reads `45 (6dlO + 12)`
- **THEN** the parsed value is `45 (6d10 + 12)`

#### Scenario: Words left alone

- **WHEN** a word token such as `Slashing` contains `S` or `l`
- **THEN** it is not changed by digit repair

#### Scenario: Resume after interruption

- **WHEN** a run is interrupted after page 120 and restarted
- **THEN** pages 1–120 are not OCR'd again

### Requirement: Monster parsing

The extractor SHALL detect a monster stat block by a fuzzy "Armor Class" label with a size/type/alignment line one or
two lines above it. It SHALL take the name only from the nearest larger-height line above on the same page, and
MUST NOT guess a name. It SHALL parse AC, HP (average and dice), speed, the six ability scores, CR and XP, and the
entry text. Variant stat blocks (for example faerie dragon colours) SHALL each become their own entity.

#### Scenario: Name not found

- **WHEN** no qualifying name line exists above a stat block on its page
- **THEN** the entity is produced with a failed `name-found` check and is not given a name from another page

#### Scenario: Variant blocks

- **WHEN** the Faerie Dragon entry lists colour variants
- **THEN** each variant is a separate entity with its own id (e.g. `mm14.monster.faerie-dragon-red`)

### Requirement: Spell parsing

The extractor SHALL parse each spell's level, school, ritual flag, casting time, range, components and duration,
and the description text, including casting times that wrap onto a second line.

#### Scenario: Reaction casting time

- **WHEN** Counterspell's casting time wraps onto a second line
- **THEN** the casting time is parsed as a reaction with its trigger and all four spell fields pass

### Requirement: Magic item parsing

The extractor SHALL parse each magic item's name (including names spanning two lines), type, rarity and attunement
requirement from its type line, plus the description text, including the artifacts section.

#### Scenario: Two-line name

- **WHEN** an item heading spans two lines ("AMULET OF PROOF AGAINST" / "DETECTION AND LOCATION")
- **THEN** one entity named "Amulet of Proof against Detection and Location" is produced

### Requirement: Self-checks

Every produced entity SHALL carry a `checks` list of `{name, passed, reason}`. The checks SHALL include at least: name
found; text non-empty; no letters left inside numeric or dice fields; unique id. Monsters: HP average equals
floor(n·(s+1)/2)+k of the dice; six ability scores present; AC, HP, CR and speed present; CR is a legal value; XP matches
CR. Spells: level and school parsed; casting time, range, components and duration present; concentration agrees with the
duration text. Magic items: type and rarity parsed. A failed check's reason MUST be human-readable and include the
values involved.

#### Scenario: HP mismatch

- **WHEN** Clay Golem's HP line is OCR'd as `133 (14d70 + 56)`
- **THEN** the `hp-matches-dice` check fails with a reason naming 133 and 14d70

#### Scenario: Clean entity

- **WHEN** every check on an entity passes
- **THEN** its `checks` list contains only passed entries

### Requirement: Provenance

Every produced entity SHALL carry `provenance` with its page numbers and, per page, the boxes (page-image pixel
coordinates at 200 dpi) covering its text.

#### Scenario: Entity on two pages

- **WHEN** a spell's text continues onto the next page
- **THEN** provenance lists both pages, each with boxes

### Requirement: Canonical output

The extractor's result SHALL be written as `books/canonical/<book-slug>.json` in the existing envelope format with
schema version "2", `dataSource` "programmatic" and `authority` "book". Ids SHALL follow
`<book-slug>.<type>.<name-slug>` with the slug from the book registry, and stay the same across re-runs of the same
book when the name is read the same. The loader SHALL accept schema versions "1" and "2".

#### Scenario: Stable ids

- **WHEN** the same book is extracted twice
- **THEN** both runs produce the same id for each entity whose name was read identically

#### Scenario: Old files still load

- **WHEN** ingestion loads another book's schema-"1" canonical file
- **THEN** it loads without error

### Requirement: Recognition report

Each run SHALL produce a report with counts per type, the share of entities passing all checks, and the share of rules
pages covered. Pages without rules text (art-only, table of contents, index, credits) are excluded from coverage. The
report SHALL include a warning when either share is under 95%. It SHALL be stored as `<book-slug>.report.json` and
returned in the run result.

#### Scenario: Low recognition

- **WHEN** 90% of a book's entities pass all checks
- **THEN** the report carries a warning naming the 90% figure

### Requirement: Run endpoint and queue lane

`POST /admin/books/{id}/extract-programmatic` (admin API key) SHALL queue an extraction run for one book on a queue lane
separate from other ingestion work, and return immediately. On success the app SHALL apply overrides, delete the book's
old canonical file and sidecars (`.errors`, `.warnings`, `.declined`, `.progress*`), write the new file and report,
delete the book's old points from `dnd_entities`, and ingest the new Accepted entities. On failure, the old files and
points stay unchanged.

#### Scenario: Successful run replaces old data

- **WHEN** a run for `mm14` completes
- **THEN** `mm14.errors.json` and `mm14.declined.json` no longer exist, `mm14.json` has schema version "2", and
  `dnd_entities` holds only the new mm14 Accepted entities

#### Scenario: Failed run keeps old data

- **WHEN** the extractor fails mid-run
- **THEN** the existing `mm14.json` and its `dnd_entities` points are unchanged

#### Scenario: Other work not blocked

- **WHEN** an extraction run is in progress
- **THEN** a block-ingestion job for another book still starts

### Requirement: Development scorer

The extractor package SHALL include a CLI scorer that compares a canonical file with a 5etools data directory and
prints per-field agreement, text coverage and text precision, for every type the extractor produces, including Class,
Subclass, Race, Subrace, Feat, Background, Weapon, Armor, Item, VehicleMount, Object, Trap, DiseasePoison and
Condition. The extractor service and the app MUST NOT read 5etools data.

#### Scenario: Scorer is dev-only

- **WHEN** the service processes a book
- **THEN** no 5etools file is opened

#### Scenario: Character types scored

- **WHEN** the scorer runs on `phb14.json` with `--type Class`
- **THEN** it prints found/expected and per-field agreement for the 12 PHB classes

#### Scenario: Equipment types scored

- **WHEN** the scorer runs on `phb14.json` with `--type Weapon`
- **THEN** it prints found/expected and per-field agreement for the PHB weapons

### Requirement: Table continuations join their table

A table whose rows continue past a column or page break SHALL stay one table in the entity that holds its start. A
repeated column header at the top of the continuation (including OCR misreads of `d100 Effect` such as "Oolp Effect")
MUST NOT start a new entity.

#### Scenario: Haunted Effects across a column break

- **WHEN** TCE's Haunted Effects d100 table continues into the next column under a repeated "d100 Effect" header
- **THEN** `tce.rule.haunted` holds rows 01-00 and no `tce.rule.haunted-oolp-effect-*` entity is produced

#### Scenario: Table across a page break

- **WHEN** the Unraveling Magic Effects table runs from one page onto the next
- **THEN** one entity holds every row and no fragment entity is produced

### Requirement: Table rows are not headings

A line that is a row of a table (it starts with the row's number or range under that table's header) MUST NOT be read
as an entity heading, whatever words it contains.

#### Scenario: Contact row mentioning a weapon

- **WHEN** GGR p69's Non-Izzet Contacts row reads "5 I helped a minor Gruul chieftain acquire an Izzet weapon."
- **THEN** no MagicItem is produced from it and the row stays in the Non-Izzet Contacts table

### Requirement: Stat block ends at its last entry

A monster stat block SHALL end after its last trait, action, bonus action, reaction or legendary action entry. Following
prose, other entities' text and later pages MUST NOT be absorbed into it, and trait or action text MUST NOT be parsed
into `languages` or another header field.

#### Scenario: Homunculus Servant stops at Channel Magic

- **WHEN** ERLW's Homunculus Servant stat block is followed by the Replicate Magic Item infusion
- **THEN** the monster's text ends after Channel Magic and its traits and actions are in `trait`, `action` and `reaction`

#### Scenario: Sacred Statue languages

- **WHEN** a Sacred Statue's Languages line is followed by its traits
- **THEN** `languages` holds only "the languages the eidolon knew in life"

### Requirement: Both columns of a stat block are read

A stat block that runs from the left column into the right column, or across a page, SHALL be read in full, left column
then right column. Its text MUST NOT end mid-sentence where a column ends.

#### Scenario: Adult dragon right column

- **WHEN** the Adult Gold Dragon's stat block continues in the right column with Breath Weapons and Legendary Actions
- **THEN** the entity's text and fields include Breath Weapons, Change Shape and its legendary actions

#### Scenario: Text does not stop mid-sentence

- **WHEN** a stat block's left column ends inside Frightful Presence
- **THEN** the entity continues with the rest of that sentence from the right column

### Requirement: Sidebars and quotes stay out

A sidebar, boxed text or pull quote printed beside or inside an entity's section MUST NOT be glued into that entity's
text or fields. One with its own heading SHALL become its own entity; a headingless quote box is left to block retrieval.

#### Scenario: Werewolf quote

- **WHEN** an all-caps quote box sits inside the Werewolf's stat block area
- **THEN** the Shapechanger trait holds only its own text and the Werewolf has its actions

#### Scenario: Schools of Magic sidebar

- **WHEN** PHB's "The Schools of Magic" sidebar sits next to Concentration
- **THEN** Concentration's text does not contain it

### Requirement: Sibling tables are separate entities

Tables that each carry their own printed title under a common heading SHALL each become their own entity named by that
title, instead of running into the previous table's entity.

#### Scenario: XGE random encounters

- **WHEN** XGE prints Arctic Encounters (Levels 1-4) through (Levels 17-20) one after another
- **THEN** each of those tables is its own entity named by its title

#### Scenario: Monsters' Desires

- **WHEN** TCE prints d4 desire tables for Aberrations, Beasts, Celestials and others under Monsters' Desires
- **THEN** each table is its own entity

### Requirement: Boundary fixes keep all content

Text that a boundary rule detaches from an entity SHALL end up in another entity: its own heading's section as a Lore or
Rule entity, or the table or entity it belongs to. A boundary rule MUST NOT be the reason printed body text appears in
no entity; headingless quote boxes are not body text.

#### Scenario: Izzet sections survive

- **WHEN** the Gruul row is no longer read as a MagicItem heading
- **THEN** "How Do I Fit In?", "An Izzet Party" and "Rank and Renown" are each in a GGR entity

#### Scenario: Artificer infusions survive

- **WHEN** the Homunculus Servant stops at Channel Magic
- **THEN** Replicate Magic Item and Group Patrons are each in an ERLW entity

### Requirement: Margin notes kept as notes

A writer's margin note printed beside an entity's text (a handwritten aside signed by Bigby or Diancastra, a Mordenkainen
note) SHALL be kept as a `notes` entry `{by, text, page}` of the entity that holds the most text on the note's page,
never in its `canonicalText`. A final line opening with a dash is the signature (`by`) and not part of the text. A note
on a page no entity holds MUST become a Lore entity that fails a check so it is reviewed. Notes MUST be carried into the
stored entity, its vector payload and the text it is embedded as (after the canonical text, under a "Notes:" line), and
MUST NOT be a reviewed field for overrides. Epigraphs, quote boxes, art captions, map labels and running heads are not
notes.

#### Scenario: Death Giant Shrouded One's margin note

- **WHEN** BGG p127 prints "Most giants know better than to make regular use of the death rune." signed "-Diancastra" beside the Death Giant Shrouded One
- **THEN** that entity's `notes` holds `{by: "Diancastra", text: "Most giants know better than to make regular use of the death rune.", page: 127}`, its `canonicalText` does not contain the sentence, and a search for the sentence's words returns the entity
