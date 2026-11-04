## ADDED Requirements

### Requirement: Disposition from checks

An extracted entity whose checks all pass SHALL get disposition `Accepted`. An entity with any failed check SHALL get
disposition `NeedsReview` and `needsReview: true`, and MUST NOT be ingested into `dnd_entities` until approved.

#### Scenario: Flagged entity not served

- **WHEN** an entity has a failed `hp-matches-dice` check
- **THEN** it is absent from `dnd_entities` and the chat cannot retrieve it

#### Scenario: Clean entity served

- **WHEN** all of an entity's checks pass
- **THEN** it is ingested into `dnd_entities` without admin action

### Requirement: Overrides file

Admin corrections and approvals SHALL be stored in `books/canonical/<book-slug>.overrides.json`. Each override records
entity id, field path, value, the extracted value it replaced, reason and timestamp. Each approval records entity id,
reason and timestamp. Overrides and approvals SHALL be applied to every run's output before the canonical file is
written, so re-runs never lose them.

#### Scenario: Override survives re-run

- **WHEN** the admin overrides Grell's CR and the book is extracted again
- **THEN** the new canonical file has the overridden CR

#### Scenario: Extraction drifts under an override

- **WHEN** a re-run extracts a value different from the one recorded under an override
- **THEN** the entity gets a failed `override-drift` check and appears in the review list

#### Scenario: Orphaned override

- **WHEN** an override's entity id is not in a run's output
- **THEN** the run result lists it and the override stays in the file

### Requirement: Admin access

`User` SHALL have an `IsAdmin` flag (default false), added by an EF migration. The admin review page SHALL require an
authorization policy that passes only for signed-in users with `IsAdmin` true.

#### Scenario: Non-admin blocked

- **WHEN** a signed-in user without `IsAdmin` opens `/admin/review`
- **THEN** access is denied

### Requirement: Admin review page

The Blazor page `/admin/review` SHALL list flagged entities (filterable by book), let the admin browse any entity, and
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
