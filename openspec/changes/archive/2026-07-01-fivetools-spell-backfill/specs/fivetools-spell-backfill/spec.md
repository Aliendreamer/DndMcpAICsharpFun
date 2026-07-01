## ADDED Requirements

### Requirement: Missing spells are backfilled from 5etools for official books

The system SHALL provide `POST /admin/books/{id}/backfill-spells` that, for a book with a
`FivetoolsSourceKey`, creates a canonical Spell entity for each 5etools spell of the book's source whose
normalized name is not already a Spell entity in the canonical. It MUST only append gaps (never modify or
overwrite existing entities), MUST mark each backfilled entity `dataSource:"5etools-backfill"`, and MUST
map the 5etools content fields (`entries` as a Description block, `entriesHigherLevel`, `damageInflict`,
`conditionInflict`) onto the Spell entity.

#### Scenario: A missing spell is backfilled
- **WHEN** the PHB canonical is missing "Regenerate" and 5etools has a PHB "Regenerate" spell
- **THEN** the endpoint appends a Spell entity id `phb14.spell.regenerate`, name "Regenerate", `dataSource:"5etools-backfill"`, with its description mapped from the 5etools entries

#### Scenario: Already-present spells are not duplicated
- **WHEN** the canonical already has a "Fireball" Spell entity
- **THEN** backfill does not append a second "Fireball" (idempotent)

#### Scenario: Homebrew book is a no-op
- **WHEN** the book has no `FivetoolsSourceKey`
- **THEN** the endpoint backfills nothing
