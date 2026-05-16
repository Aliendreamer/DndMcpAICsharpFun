## ADDED Requirements

### Requirement: Entity schema carries SRD availability flags
Canonical JSON entity objects SHALL support three optional boolean top-level fields: `srd` (SRD 5.1), `srd52` (SRD 5.2.1), `basicRules2024` (Basic Rules 5.5e/2024). Absent fields SHALL be treated as `false`.

#### Scenario: SRD 5.2.1 entity in canonical JSON

- **WHEN** a canonical JSON entity has `"srd52": true`
- **THEN** ingestion writes `srd52=true` to the Qdrant entity payload

#### Scenario: Entity without SRD fields

- **WHEN** a canonical JSON entity has no `srd`, `srd52`, or `basicRules2024` fields
- **THEN** Qdrant payload stores `srd=false`, `srd52=false`, `basicRules2024=false`

### Requirement: 5etools ingestion populates SRD flags from source JSON
When ingesting entities from 5etools JSON files, the mapper SHALL read `srd`, `srd52`, and `basicRules2024` fields from each entity element and write them to the `EntityEnvelope`.

#### Scenario: Spell with srd52 in 5etools JSON

- **WHEN** a 5etools spell JSON element has `"srd52": true`
- **THEN** the mapped `EntityEnvelope` has `Srd52=true`

#### Scenario: Monster with legacy srd flag

- **WHEN** a 5etools monster JSON element has `"srd": true`
- **THEN** the mapped `EntityEnvelope` has `Srd=true`

### Requirement: Qdrant entity payload indexes SRD flags
The `dnd_entities` Qdrant collection SHALL index `srd`, `srd52`, and `basicRules2024` as keyword (boolean) payload fields, enabling filtering.

#### Scenario: Filter by srd52

- **WHEN** an entity search includes filter `srd52=true`
- **THEN** only entities with `srd52=true` in their payload are returned

### Requirement: Entity search endpoints accept SRD filter parameters
`GET /retrieval/entities/search` and `GET /admin/retrieval/entities/search` SHALL accept optional query parameters `srd`, `srd52`, `basicRules2024` (boolean).

#### Scenario: Public search filtered to free rules

- **WHEN** `GET /retrieval/entities/search?srd52=true&q=fireball` is called
- **THEN** only entities with `srd52=true` matching "fireball" are returned
