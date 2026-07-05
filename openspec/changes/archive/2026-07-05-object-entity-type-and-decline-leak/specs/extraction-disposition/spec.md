## ADDED Requirements

### Requirement: Uncertain extractions are declined, not persisted as empty shells

When an LLM extraction result signals that the model could not confidently produce an entity — indicated by a `none`/ambiguous type, empty or meaningless `fields`, or a `canonicalText` that is classification *reasoning* rather than entity content — the system SHALL NOT persist an entity for that candidate. It SHALL instead record the candidate as an extraction failure (in `errors.json`) or a decline (in `declined.json`), so the outcome is auditable and re-triable via the `errorsOnly` retry.

#### Scenario: Reasoning-only response does not become an entity

- **WHEN** the LLM returns empty `fields` together with a `canonicalText` that describes why it could not classify the candidate (e.g. "the schema doesn't include an 'object' type… safer to classify as 'none'")
- **THEN** no entity is written for that candidate, and the candidate is recorded as an extraction failure or decline

#### Scenario: A genuinely sparse but valid entity is still kept

- **WHEN** an extraction produces at least one meaningful field and no ambiguity signal
- **THEN** the entity is persisted normally

#### Scenario: Declined uncertain candidate is re-triable

- **WHEN** an uncertain extraction has been recorded as a failure
- **THEN** it appears in the errors file and is re-processed by an `errorsOnly` re-extraction
