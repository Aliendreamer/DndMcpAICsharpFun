## MODIFIED Requirements
### Requirement: Every structured entity record SHALL conform to a common envelope

The system SHALL define a common entity envelope used by every entity record across all 20 entity types. The envelope SHALL contain the following fields: `id` (string), `type` (string, one of the 20 entity types), `name` (string), `sourceBook` (string), `edition` (string, one of `Edition2014`, `Edition2024`, or other recognized editions), `page` (integer or null), `firstAppearedIn` (object with `book`, `edition`, optional `page`), `revisedIn` (array of objects each with `book`, `edition`, `summary`), `settingTags` (array of strings), `keywords` (array of strings, defaults to empty), `canonicalText` (string), and `fields` (object containing type-specific fields).

#### Scenario: Envelope shape is consistent across types

- **WHEN** any entity record is loaded from a canonical JSON file
- **THEN** it has all envelope fields present (`fields` may be empty for trivial types but the key SHALL exist; `keywords` defaults to `[]` when absent)

#### Scenario: Missing required envelope field fails validation

- **WHEN** an entity record is loaded that lacks `id`, `type`, `name`, `sourceBook`, `edition`, `canonicalText`, or `fields`
- **THEN** the loader SHALL reject the record and surface a validation error identifying the missing field

#### Scenario: Unknown entity type fails validation

- **WHEN** a record has `type` that is not one of the 20 supported entity types
- **THEN** the loader SHALL reject the record with an error naming the unsupported type

#### Scenario: Entity with keywords round-trips through canonical JSON

- **WHEN** an entity with `keywords = ["Pack Tactics", "Keen Senses"]` is serialised to canonical JSON and re-loaded
- **THEN** the loaded entity has the same `keywords` array
