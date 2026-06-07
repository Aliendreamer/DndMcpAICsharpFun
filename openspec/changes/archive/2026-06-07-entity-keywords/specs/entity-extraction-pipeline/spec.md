## ADDED Requirements
### Requirement: Extraction SHALL produce canonical JSON entities with a `keywords` field for Monster type

When the LLM extraction pipeline processes a Monster entity, the resulting canonical JSON record SHALL include a `keywords` array under `fields` containing the names of notable traits visible in the stat block (e.g. `"Pack Tactics"`, `"Amphibious"`, `"Undead Fortitude"`). The array SHALL be empty when no notable traits are identified. Non-Monster entity types are not required to produce `keywords`.

#### Scenario: Monster extraction produces keywords from trait names

- **WHEN** the LLM extracts a monster whose stat block includes traits named `"Pack Tactics"` and `"Keen Senses"`
- **THEN** the resulting canonical JSON entity has `"fields": { ..., "keywords": ["Pack Tactics", "Keen Senses"] }`

#### Scenario: Monster with no notable traits produces empty keywords

- **WHEN** the LLM extracts a monster with no named traits (e.g. a simple creature)
- **THEN** the resulting canonical JSON entity has `"fields": { ..., "keywords": [] }`

#### Scenario: Non-Monster entities omit keywords field

- **WHEN** the LLM extracts a Spell or Class entity
- **THEN** the resulting canonical JSON entity does not include a `keywords` field under `fields`
