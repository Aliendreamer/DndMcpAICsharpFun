## ADDED Requirements

### Requirement: Deterministic 5etools monster backfill

The system SHALL provide `POST /admin/books/{id}/backfill-monsters` that appends any monster present in
the book's authoritative 5etools roster but missing from its canonical, projecting each from 5etools
data via the existing monster mapper and marking it `dataSource: "5etools-backfill"`. The operation
MUST be gap-only (never overwrite or duplicate an existing canonical monster) and idempotent (a second
run with no new gaps changes nothing).

#### Scenario: Missing roster monster is backfilled

- **WHEN** the Monster Manual's canonical is missing a monster that exists in the 5etools `MM` roster
- **THEN** the endpoint appends that monster to `mm14.json` marked `dataSource:"5etools-backfill"`

#### Scenario: Backfill is gap-only and idempotent

- **WHEN** `backfill-monsters` runs and every roster monster is already present in the canonical
- **THEN** the canonical is unchanged and no duplicates are created

#### Scenario: Grounded monsters are preserved

- **WHEN** a monster was already extracted from the PDF (grounded) and also exists in the roster
- **THEN** backfill does not replace the grounded entity with a 5etools-sourced one
