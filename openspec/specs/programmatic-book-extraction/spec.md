# programmatic-book-extraction Specification

## Purpose
TBD - created by archiving change programmatic-extraction-phase1. Update Purpose after archive.
## Requirements
### Requirement: Deterministic extractor service

The system SHALL provide a book extractor service, separate from the app container, that reads a book PDF and produces
Monster, Spell and MagicItem entities using rules only. It MUST NOT call any LLM and MUST NOT use a GPU. It SHALL
read PDFs from a read-only mount of `books/`.

#### Scenario: Extraction without a model

- **WHEN** the extractor processes a book PDF
- **THEN** it produces entities without any network call to Ollama or another LLM service

#### Scenario: Read-only books

- **WHEN** the extractor runs
- **THEN** it cannot modify any file under `books/`

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
prints per-field agreement and text coverage. The extractor service and the app MUST NOT read 5etools data.

#### Scenario: Scorer is dev-only

- **WHEN** the service processes a book
- **THEN** no 5etools file is opened

