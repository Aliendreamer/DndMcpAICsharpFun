# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
