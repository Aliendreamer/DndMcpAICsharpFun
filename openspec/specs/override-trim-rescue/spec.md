# override-trim-rescue Specification

## Purpose
TBD - created by archiving change override-trim-rescue. Update Purpose after archive.

## Requirements

### Requirement: Trimmed text is kept

When overrides are applied, every printed line that a `canonicalText` override removes from its entity and that no
other entity of the book holds SHALL be kept in a Lore entity flagged for review, instead of being dropped.

#### Scenario: Lore trimmed from a stat block

- **WHEN** a Monster's reviewed `canonicalText` override removes three paragraphs of lore that no other entity holds
- **THEN** after applying overrides the book holds a Lore entity with those paragraphs, flagged NeedsReview, whose
  failed check names the Monster

#### Scenario: Trimmed text held elsewhere

- **WHEN** the lines an override removes are all held by another entity of the book
- **THEN** no rescue entity is created

#### Scenario: Short label removed

- **WHEN** an override removes only a page header or a map label shorter than 30 letters and digits
- **THEN** no rescue entity is created

### Requirement: Rescue entity is stable and reviewable

The rescue entity's id SHALL be derived from its host's id, so overrides, approvals and declines recorded for it apply
on every later run. Rescue entities SHALL be recomputed on every apply: none is duplicated, and one disappears when
its text is held again.

#### Scenario: Declined rescue stays declined

- **WHEN** the admin declines a rescue entity and the book is extracted again
- **THEN** the rescue entity is recreated with the same id and is Declined

#### Scenario: Text held again

- **WHEN** a later extraction puts the trimmed lines into their own entity
- **THEN** the next apply creates no rescue entity for that host
