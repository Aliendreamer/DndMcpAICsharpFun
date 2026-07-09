# structured-knowledge-store Specification

## Purpose
TBD - created by archiving change character-fact-resolution. Update Purpose after archive.
## Requirements
### Requirement: First-class Table and ChoiceSet canonical shapes

The canonical model SHALL represent a `Table` (named columns + rows of typed cells) and a
`ChoiceSet` (a named set of options) as first-class entity shapes, distinct from the flat
`entity{type, fields}` shape, so relational/tabular content is no longer forced into stat-block
fields.

#### Scenario: Authoring the Draconic Ancestry table
- **WHEN** the Draconic Ancestry table is authored into canonical JSON
- **THEN** it is stored as a `Table` with columns (ancestry, damage type, breath area, save ability) and one row per ancestry (Black, Blue, Brass, … Red, …), not as a Monster entity

#### Scenario: Authoring the ancestry choice-set
- **WHEN** the draconic-ancestry choice is authored
- **THEN** it is stored as a `ChoiceSet` whose options reference the corresponding rows of the Draconic Ancestry table

### Requirement: Per-cell provenance

Each authored fact (table cell, choice option) SHALL carry a provenance reference of
`{blockId, sourceBook, page}` identifying the prose chunk it derives from. Provenance is a stored
reference, not a copy of the prose (prose remains in Qdrant `dnd_blocks`).

#### Scenario: A table cell cites its source
- **WHEN** the "Red → fire, 15-ft cone" row is authored
- **THEN** each cell carries `{blockId, sourceBook: "PHB", page: 34}` (or the actual chunk) so a consumer can render a citation and fetch the prose span

#### Scenario: Provenance survives projection
- **WHEN** a table is projected into Postgres
- **THEN** the provenance reference is stored on the corresponding row/cell, retrievable with the fact

### Requirement: Projection of canonical tables/choice-sets into Postgres

The system SHALL project authored `Table` and `ChoiceSet` canonical JSON into queryable Postgres
tables (`StructuredEntity`, `StructuredTable`, `StructuredTableRow`, `ChoiceSet`) via an idempotent
ingest step, so structured facts are answerable by keyed lookup and join.

#### Scenario: Ingest is idempotent
- **WHEN** the Dragonborn canonical tables are projected twice
- **THEN** the second run upserts (no duplicate rows); the Postgres state is identical

#### Scenario: A cell is retrievable by key
- **WHEN** the projection has run and a consumer queries the Draconic Ancestry table for `ancestry = 'Red'`
- **THEN** exactly one row returns with damage type, breath area, save ability, and its provenance

