## ADDED Requirements
### Requirement: Per-field merge during canonical ingest
When `ingest-entities` runs and a Qdrant point already exists for an entity ID, the system SHALL merge the canonical and existing data using per-field priority rules before upserting.

Field priority rules:

- `fields`, `canonicalText`, `firstAppearedIn` → canonical always wins
- `type` → existing (5etools) wins if present and canonical type is `Class`
- `srd`, `srd52`, `basicRules2024` → existing (5etools) wins
- `keywords` → whichever list is longer wins
- `page` → existing wins if set; canonical wins if existing has no page
- `DataSource` → always set to `"llm"` after merge

#### Scenario: Canonical ingested after 5etools

- **WHEN** a canonical entity is ingested and a 5etools point exists with the same entity ID
- **THEN** the merged point SHALL have canonical `fields` and `canonicalText`
- **THEN** the merged point SHALL have 5etools `srd`, `srd52`, `basicRules2024` flags
- **THEN** the merged point `DataSource` SHALL be `"llm"`

#### Scenario: No existing point

- **WHEN** a canonical entity is ingested and no existing Qdrant point exists for that entity ID
- **THEN** the canonical entity SHALL be upserted as-is without any merge

#### Scenario: Keyword merge takes longer list

- **WHEN** the existing point has `keywords: ["fighter", "martial"]` and canonical has `keywords: ["fighter", "martial", "warrior"]`
- **THEN** the merged point SHALL have `keywords: ["fighter", "martial", "warrior"]`

### Requirement: EntityMerger is independently testable
The merge logic SHALL be encapsulated in a static `EntityMerger` class with a single public method `Merge(EntityEnvelope canonical, EntityEnvelope existing): EntityEnvelope`. It SHALL have no dependencies on Qdrant or the ingestion pipeline.

#### Scenario: Merge method is pure

- **WHEN** `EntityMerger.Merge(canonical, existing)` is called
- **THEN** neither input envelope SHALL be mutated
- **THEN** the returned envelope SHALL be a new instance with merged fields

### Requirement: Batch pre-fetch before merge
`EntityIngestionOrchestrator` SHALL batch-fetch existing Qdrant points for all entity IDs in a canonical file before processing, using a single `GetByIdsAsync` call.

#### Scenario: Batch fetch is a single round-trip

- **WHEN** `ingest-entities` runs for a book with 400 entities
- **THEN** exactly one `GetByIdsAsync` call SHALL be made to fetch existing data for all 400 IDs
