## MODIFIED Requirements

### Requirement: `canonicalText` SHALL be the embedded representation

Every entity SHALL include a `canonicalText` string. Its pieces SHALL be the text passed to the embedding model when the entity is ingested into the entity vector store. The format is per-type and SHALL be deterministic given the structured `fields` (so re-rendering an unchanged record yields identical `canonicalText`).

#### Scenario: Spell canonicalText includes header and body

- **WHEN** a Spell record is loaded
- **THEN** `canonicalText` includes the spell name, level, school, casting time, range, components, duration, full description, and `at higher levels` text

#### Scenario: Re-rendering an unchanged record yields identical canonicalText

- **WHEN** an entity record's `fields` are unchanged but the record is re-rendered through the canonical-text generator
- **THEN** the output `canonicalText` is byte-for-byte identical to the previous one
