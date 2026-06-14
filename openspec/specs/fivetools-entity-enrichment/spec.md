# fivetools-entity-enrichment Specification

## Purpose
TBD - created by archiving change fivetools-entity-enrichment. Update Purpose after archive.
## Requirements
### Requirement: Entities are enriched from 5etools source files at ingest

During `ingest-entities`, the system SHALL look up each entity's matching 5etools record by exact entity id and, when found, use it as the enrichment source for `EntityMerger`. 5etools records SHALL be sourced by mapping the local `5etools/*.json` files via the existing `FivetoolsSourceRegistry` and mappers into an in-memory id→envelope index. The system SHALL NOT upsert 5etools-only records into `dnd_entities`; only entities present in the canonical book file are ingested. If the `5etools/` files are absent, ingestion SHALL proceed without enrichment and log that enrichment was skipped.

#### Scenario: Matching 5etools record enriches the entity

- **WHEN** a canonical entity `phb14.spell.fireball` is ingested and the 5etools files contain a record mapping to id `phb14.spell.fireball`
- **THEN** the ingested entity SHALL be the result of merging the canonical entity with that 5etools record

#### Scenario: No 5etools match leaves the entity unchanged

- **WHEN** a canonical entity has no 5etools record with the same id
- **THEN** the entity SHALL be ingested without enrichment (no fields altered by enrichment)

#### Scenario: No 5etools-only entities are added

- **WHEN** the 5etools files contain records whose ids are not present in the book being ingested
- **THEN** those 5etools-only records SHALL NOT be added to `dnd_entities`

#### Scenario: Enrichment is skipped gracefully when files are absent

- **WHEN** `ingest-entities` runs and the `5etools/` directory is not present
- **THEN** ingestion SHALL complete using the canonical entities as-is, and SHALL log that enrichment was skipped

### Requirement: Ingest reports 5etools enrichment coverage

`ingest-entities` SHALL report, per book, the number of entities enriched (matched to a 5etools record) and the number left unmatched.

#### Scenario: Coverage counts are reported

- **WHEN** a book with 400 entities is ingested and 360 match a 5etools record
- **THEN** the ingest result SHALL report 360 enriched and 40 unmatched

