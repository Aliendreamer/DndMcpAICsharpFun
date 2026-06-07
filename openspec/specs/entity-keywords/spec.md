# entity-keywords Specification

## Purpose
TBD - created by archiving change entity-keywords. Update Purpose after archive.
## Requirements
### Requirement: Entity keywords SHALL be populated from 5etools traitTags for Monster entities

When 5etools JSON is imported, the system SHALL map the `traitTags` array from each monster record to the `keywords` field on the resulting `EntityEnvelope`. Non-Monster 5etools entity types SHALL receive an empty `keywords` list.

#### Scenario: Monster with traitTags gets keywords

- **WHEN** a 5etools monster record has `"traitTags": ["Amphibious", "Pack Tactics"]`
- **THEN** the ingested entity in `dnd_entities` has `keywords = ["Amphibious", "Pack Tactics"]`

#### Scenario: Monster with no traitTags gets empty keywords

- **WHEN** a 5etools monster record has no `traitTags` field
- **THEN** the ingested entity has `keywords = []`

#### Scenario: Non-Monster 5etools entity gets empty keywords

- **WHEN** a 5etools Spell or Class entity is imported
- **THEN** the ingested entity has `keywords = []`

### Requirement: Entity keywords SHALL be populated from the `keywords` field in canonical JSON

When a canonical JSON file is ingested, each entity record's `fields.keywords` array (if present) SHALL be read and stored as the entity's `keywords`. If the field is absent, the entity SHALL have `keywords = []`.

#### Scenario: Canonical JSON entity with keywords field is ingested

- **WHEN** a canonical JSON entity has `"fields": { "keywords": ["Pack Tactics", "Keen Senses"] }`
- **THEN** the ingested entity in `dnd_entities` has `keywords = ["Pack Tactics", "Keen Senses"]`

#### Scenario: Canonical JSON entity without keywords field is ingested

- **WHEN** a canonical JSON entity has no `keywords` key under `fields`
- **THEN** the ingested entity has `keywords = []` without error

### Requirement: Entity search SHALL filter by keyword using exact match

The `GET /retrieval/entities/search` and `GET /admin/retrieval/entities/search` endpoints SHALL support a `?keyword=<value>` query parameter that restricts results to entities whose `keywords` array contains an exact case-sensitive match for the supplied value.

#### Scenario: Keyword filter returns only matching entities

- **WHEN** `?keyword=Amphibious` is supplied
- **THEN** only entities whose `keywords` contains `"Amphibious"` are returned

#### Scenario: Absent keyword param returns all matching entities

- **WHEN** no `?keyword=` param is supplied
- **THEN** the filter is not applied and results are not restricted by keywords

#### Scenario: Keyword filter combined with type filter

- **WHEN** `?type=Monster&keyword=Pack+Tactics` is supplied
- **THEN** only Monster entities with `"Pack Tactics"` in their keywords are returned

