## ADDED Requirements

### Requirement: 5etools patches only missing allowlisted structured fields, never overwriting extraction

The system SHALL, for a canonical entity extraction already produced, merge in from the matching 5etools
record ONLY the structured fields named in that entity type's allowlist that the entity is currently
missing. It SHALL NOT overwrite any field the entity already has, and SHALL NOT fill prose/`entries` — an
allowlist SHALL contain structured mechanics only, never `entries`. Extraction remains the source of truth
for the entity's content.

#### Scenario: A prose class gains its structured mechanics

- **WHEN** the fill runs on a book whose canonical Class entity has only `entries` and the matching 5etools class record carries `hd`, `classFeatures`, and `subclassTitle`
- **THEN** those allowlisted fields SHALL be added to the canonical entity and its `entries` SHALL be left unchanged

#### Scenario: A field extraction already produced is never overwritten

- **WHEN** the fill runs and the extraction entity already has an allowlisted field (e.g. a monster's `cr`)
- **THEN** the fill SHALL leave that field exactly as extraction produced it

### Requirement: The fill is type-agnostic, driven by a per-type structured-field allowlist

The fill SHALL apply to any entity type that has a structured-field allowlist entry, using the existing
5etools source/roster and field-mapping machinery. A type with no allowlist entry, and a type whose
entities are already complete, SHALL result in no changes. A book with no 5etools source key SHALL be a
clean no-op.

#### Scenario: A type extraction already fills is a no-op

- **WHEN** the fill runs over a type whose canonical entities already carry all their allowlisted fields
- **THEN** no canonical entity of that type SHALL be modified

#### Scenario: A homebrew book is a no-op

- **WHEN** the fill runs on a book that has no 5etools source key
- **THEN** no canonical entity SHALL be modified

### Requirement: Fills are provenance-tracked, idempotent, and lose to extraction and human edits

The system SHALL record on each entity, in a `fivetoolsFilledFields` list, the allowlisted field names it
filled from 5etools, and SHALL keep the entity's `dataSource` as the extraction source (not relabel it as
5etools). A field present and listed in `fivetoolsFilledFields` SHALL be re-derived from 5etools (same
deterministic value); a field present and NOT listed SHALL never be touched; an entity whose `dataSource`
is `manual` SHALL be skipped entirely. Running the fill twice SHALL produce a byte-identical canonical.

#### Scenario: Re-running the fill changes nothing

- **WHEN** the fill runs a second time on an already-filled canonical
- **THEN** the canonical file SHALL be byte-identical to after the first run

#### Scenario: A hand-corrected entity is left alone

- **WHEN** the fill encounters a canonical entity marked `dataSource:"manual"`
- **THEN** that entity SHALL be skipped and none of its fields changed

### Requirement: The fill runs automatically after extraction and cannot silently decay

The field-fill SHALL run automatically when `extract-entities` completes for a book with a 5etools source
key, merging into the same canonical extraction just wrote. Because the 5etools data and the allowlist are
fixed, a forced re-extraction that regenerates the prose canonical SHALL be followed by the fill
re-deriving the identical result, so the structured fields are never durably lost.

#### Scenario: A forced re-extract does not drop the fill

- **WHEN** `extract-entities?force=true` regenerates a book's prose canonical
- **THEN** the auto-run fill SHALL re-add the same allowlisted structured fields, leaving the book's canonical structurally equivalent to before the re-extract

### Requirement: An admin endpoint runs the fill on an already-extracted book

The system SHALL provide `POST /admin/books/{id}/fill-fields` that runs the field-fill on the book's
existing canonical without re-extracting, and returns a report of the entities touched and the fields
filled per type. The endpoint SHALL require the admin API key. For a book with no 5etools source key it
SHALL succeed with a no-op report.

#### Scenario: Filling an already-extracted book

- **WHEN** `POST /admin/books/{id}/fill-fields` is called for an extracted official book with missing structured fields
- **THEN** the canonical SHALL be updated with the allowlisted fields and the response SHALL report the count of entities and fields filled per type

### Requirement: The canonical fill write is safe and loadable

A fill that modifies the canonical SHALL write atomically (temp file + rename), SHALL preserve the
unique-id invariant, and SHALL produce a file that `CanonicalJsonLoader` loads without error. A failed
fill SHALL not leave a partially-written canonical.

#### Scenario: The filled canonical round-trips

- **WHEN** the fill writes an updated canonical
- **THEN** every entity id SHALL remain unique and `CanonicalJsonLoader` SHALL load the file successfully
