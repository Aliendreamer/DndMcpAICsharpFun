# entity-verification-review Specification

## Purpose
TBD - created by archiving change programmatic-extraction-phase1. Update Purpose after archive.

## Requirements

### Requirement: Disposition from checks

An extracted entity whose checks all pass SHALL get disposition `Accepted`. An entity with any failed check SHALL get
disposition `NeedsReview` and `needsReview: true`, and MUST NOT be ingested into `dnd_entities` until approved, except that a
failed `entry-level` check is advisory: when every failed, unwaived check of an entity is `entry-level` or
`name-words-known`, and `entry-level` is among them, the entity SHALL get disposition `Accepted` and keep those checks and
their reasons.

#### Scenario: Flagged entity not served

- **WHEN** an entity has a failed `hp-matches-dice` check
- **THEN** it is absent from `dnd_entities` and the chat cannot retrieve it

#### Scenario: Clean entity served

- **WHEN** all of an entity's checks pass
- **THEN** it is ingested into `dnd_entities` without admin action

#### Scenario: Entry-level alone is advisory

- **WHEN** an entity's only failed check is `entry-level`
- **THEN** it gets disposition `Accepted`, `needsReview: false`, and its failed `entry-level` check is kept

#### Scenario: Entry-level with name-words-known is advisory

- **WHEN** an entity's only failed checks are `entry-level` and `name-words-known`
- **THEN** it gets disposition `Accepted`

#### Scenario: Name-words-known alone still blocks

- **WHEN** an entity's only failed check is `name-words-known`
- **THEN** it gets disposition `NeedsReview`

#### Scenario: Entry-level with another check still blocks

- **WHEN** an entity fails `entry-level` and `text-complete`
- **THEN** it gets disposition `NeedsReview`

### Requirement: Overrides file

Admin corrections and approvals SHALL be stored in `books/canonical/<book-slug>.overrides.json`. Each override records
entity id, field path, value, the extracted value it replaced, reason and timestamp. Each approval records entity id,
reason and timestamp. Overrides and approvals SHALL be applied to every run's output before the canonical file is
written, so re-runs never lose them.

#### Scenario: Override survives re-run

- **WHEN** the admin overrides Grell's CR and the book is extracted again
- **THEN** the new canonical file has the overridden CR

#### Scenario: Extraction drifts under an override

- **WHEN** a re-run extracts a value that differs from both the one recorded under an override and the override's own
  value, and is not closer to the override's value than the recorded one was
- **THEN** the entity gets a failed `override-drift` check and appears in the review list

#### Scenario: Orphaned override

- **WHEN** an override's entity id is not in a run's output and no certain match for it exists
- **THEN** the run result lists it and the override stays in the file

### Requirement: Admin access

`User` SHALL have an `IsAdmin` flag (default false), added by an EF migration. The admin review page SHALL require an
authorization policy that passes only for signed-in users with `IsAdmin` true.

#### Scenario: Non-admin blocked

- **WHEN** a signed-in user without `IsAdmin` opens `/review`
- **THEN** access is denied

### Requirement: Admin review page

The Blazor page `/review` SHALL list flagged entities (filterable by book), let the admin browse any entity, and
show an entity's fields and failed-check reasons next to its book page image with its provenance boxes highlighted.
The admin SHALL be able to override any field of any entity (Accepted ones included) with a reason, and approve a
flagged entity. There is no bulk approve.

#### Scenario: Approve a flagged entity

- **WHEN** the admin approves a flagged entity
- **THEN** an approval is saved to the overrides file, the entity becomes `Accepted`, and it is ingested into
  `dnd_entities`

#### Scenario: Fix a misprint on a verified entity

- **WHEN** the admin overrides a field on an Accepted entity
- **THEN** the canonical file and the entity's `dnd_entities` point show the new value

#### Scenario: Page image shown

- **WHEN** the admin opens an entity
- **THEN** the page image for its first provenance page is shown with its boxes highlighted

### Requirement: Lead-in-only Lore is declined

A programmatic `Lore` entity whose entries are only an introduction sentence or a back-cover blurb SHALL get disposition
`Declined` and `needsReview: false`, regardless of its checks or approval. It MUST NOT be ingested into `dnd_entities` and
MUST NOT be listed as flagged on the review page.

#### Scenario: Introduction sentence

- **WHEN** a Lore entity's only entry is "This section presents one new subclass option: the Path of the Giant for the barbarian."
- **THEN** its disposition is `Declined`

#### Scenario: Back-cover blurb

- **WHEN** a Lore entity's only entry ends "for the world's greatest roleplaying game"
- **THEN** its disposition is `Declined`

#### Scenario: One-sentence rule is kept

- **WHEN** a Lore entity's only entry is "You gain one 9th-level spell slot, provided that you already have one."
- **THEN** its disposition is derived from its checks as before

#### Scenario: Longer text is kept

- **WHEN** a Lore entity's entries hold more than one sentence
- **THEN** its disposition is derived from its checks as before

### Requirement: Override retires on match

When a run extracts the override's value for its field — equal once whitespace, line breaks and end-of-line hyphenation
are ignored — the override SHALL be removed from the overrides file and the entity MUST NOT get an `override-drift`
check for it. The run result SHALL count retired overrides per book.

#### Scenario: Better extraction matches the correction

- **WHEN** the admin corrected Concentration's text and a later run extracts that same text with different line breaks
- **THEN** the override is removed, the entity has no `override-drift` check, and its disposition follows its checks and approval

#### Scenario: Extraction gets worse

- **WHEN** a run extracts a value that differs from both the recorded extracted value and the override value, and is no
  closer to the override value than the recorded value was
- **THEN** the override stays and the entity gets a failed `override-drift` check

### Requirement: Improved extraction keeps the override quietly

When a run extracts a value that differs from both the recorded extracted value and the override value but is closer to
the override value than the recorded value was (by token-F1 for text), the override SHALL stay and be applied, and the
entity MUST NOT get an `override-drift` check for it.

#### Scenario: Partial improvement

- **WHEN** a fix removes a glued sidebar from Concentration but the new text still differs from the correction by one word
- **THEN** the override stays applied and the entity is not sent to review for drift

#### Scenario: Unchanged extraction keeps the override

- **WHEN** a run extracts the same value the override recorded as replaced
- **THEN** the override stays, is applied, and no `override-drift` check is added

### Requirement: Orphaned overrides follow a certain match

When an override's or approval's entity id is not in a run's output, it SHALL move to the run's entity of the same
book and type whose pages include the orphan entity's page in the previous canonical file and whose name is the only
close match. Otherwise it stays orphaned. The run result SHALL list moved and orphaned entries.

#### Scenario: Name fix changes the id

- **WHEN** `phb14.rule.caasting-time` has an approval and a run now reads the name as "Casting Time"
- **THEN** the approval moves to `phb14.rule.casting-time` and the run result lists the move

#### Scenario: Split entity is not moved

- **WHEN** a boundary fix splits the old Homunculus Servant entity into a monster and several infusion entities
- **THEN** the overrides of the old entity are listed as orphans unless exactly one new entity is a certain match

#### Scenario: Two candidates

- **WHEN** an orphan's page holds two new entities of its type with equally close names
- **THEN** nothing moves and the orphan is listed

#### Scenario: Entity gone for good

- **WHEN** a run no longer produces any entity of the orphan's type on its previous page
- **THEN** the override stays in the file and the run result lists it as orphaned
