## MODIFIED Requirements

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
