## ADDED Requirements

### Requirement: Every modeled catalog type SHALL be backfillable from 5etools

For an official book, the gap-only backfill SHALL create a `5etools-backfill` entity for each 5etools roster element (of the book's source key) of a modeled catalog type — Feat, Background, Item, Weapon, Armor, Condition, Trap, DiseasePoison, VehicleMount — whose normalized name is absent from the canonical, without modifying or deleting any existing (extraction-owned or prior-backfill) entity.

#### Scenario: A feat 5etools has but the canonical lacks is backfilled

- **WHEN** backfill runs on a book whose 5etools source has feats absent from its canonical
- **THEN** each missing feat is appended as an `Accepted` entity with `dataSource:"5etools-backfill"`, and every pre-existing entity is byte-unchanged

#### Scenario: Base-item gear is partitioned across Item/Weapon/Armor without double-count

- **WHEN** the Item, Weapon, and Armor providers enumerate the base-item roster
- **THEN** each base item is claimed by exactly one provider (per its 5etools type code), with no item missing and none double-counted

#### Scenario: Backfill apply preserves unique ids and reloads

- **WHEN** a multi-type backfill is applied to a canonical file
- **THEN** all entity ids are unique and the written file reloads through `CanonicalJsonLoader` without a duplicate-id error

### Requirement: Coverage against 5etools SHALL be measured and reported per book and type

The system SHALL compute, per official book and per modeled type, the 5etools roster count, the present count, and the NAMED missing entities, plus a bucket of 5etools content types not modeled at all — WITHOUT applying any backfill. This SHALL be exposed via an admin coverage endpoint, a warning summary in canonical validation, and a startup log. Coverage SHALL never block ingest or validation (report + warning only).

#### Scenario: Coverage report names the gaps

- **WHEN** `GET /admin/books/{id}/coverage` is called for an official book
- **THEN** the response lists per type the roster count, present count, and the names of the missing entities, and an unmodeled bucket that includes the optionalfeatures 5etools has for the book

#### Scenario: Below-100% coverage warns but does not block

- **WHEN** a book is below full 5etools coverage
- **THEN** canonical validation still returns success (not 422) with a coverage warning summary, and startup logs a coverage warning for that book — nothing is blocked

#### Scenario: Non-official book is a no-op

- **WHEN** coverage is requested for a book with no 5etools source key
- **THEN** the result is empty/no-op with no error
