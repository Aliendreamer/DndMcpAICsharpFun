## MODIFIED Requirements

### Requirement: Extraction writes source field from registered source key
When `POST /admin/books/{id}/extract-entities` runs and the book's `IngestionRecord` has a non-null `FivetoolsSourceKey`, each entity written to the canonical JSON SHALL have its `source` field set to that key. When `FivetoolsSourceKey` is null, `source` SHALL remain empty.

#### Scenario: Extraction with bound source key

- **WHEN** extraction runs for a book with `FivetoolsSourceKey="XDMG"`
- **THEN** every entity in the output canonical JSON has `"source": "XDMG"`

#### Scenario: Extraction without source key

- **WHEN** extraction runs for a book with `FivetoolsSourceKey=null`
- **THEN** entity `source` fields are empty strings (existing behaviour unchanged)

### Requirement: Extraction derives edition from source key via registry
When `FivetoolsSourceKey` is non-null, the entity `edition` field SHALL be derived from the `PublishedYear` in `BookSourceRegistry`: year ≥ 2024 → `"Edition2024"`, year < 2024 → `"Edition2014"`.

#### Scenario: 2024 source key sets Edition2024

- **WHEN** extraction runs for a book with `FivetoolsSourceKey="XPHB"` (published 2024)
- **THEN** all extracted entities have `"edition": "Edition2024"`

#### Scenario: 2014 source key sets Edition2014

- **WHEN** extraction runs for a book with `FivetoolsSourceKey="PHB"` (published 2014)
- **THEN** all extracted entities have `"edition": "Edition2014"`
