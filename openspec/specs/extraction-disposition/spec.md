# extraction-disposition Specification

## Purpose
TBD - created by archiving change extraction-honesty. Update Purpose after archive.
## Requirements
### Requirement: Each extracted candidate carries a disposition

Every processed extraction candidate SHALL be assigned exactly one disposition: `Accepted` (typed,
grounded, eligible for `dnd_entities`), `NeedsReview` (emitted but ungrounded, or low/medium
confidence, or OCR-noisy name, or an ambiguous decline), `Ungrounded` (a Tier-2-judge-confirmed
ungrounded fabrication — the model emitted an entity but its fields are not supported by the source
prose; excluded from `dnd_entities`, retained in canonical for audit, distinct from a model-chosen
`Declined`), `Declined` (the model chose `none` on clearly non-entity content), or `Failed` (no
valid output after retries). The disposition SHALL be persisted on the canonical-JSON entity record
(replacing the `needsReview` boolean as the trust signal).

#### Scenario: Grounded, confident entity is Accepted

- **WHEN** an entity is typed by the model, grounded by the gate, and not flagged by name/confidence
- **THEN** its disposition is `Accepted`

#### Scenario: Ungrounded entity is NeedsReview

- **WHEN** an entity is emitted but the grounding gate cannot ground its key fields
- **THEN** its disposition is `NeedsReview`

#### Scenario: Judge-confirmed fabrication is Ungrounded

- **WHEN** the Tier 2 judge confirms an emitted entity's fields are not supported by the source prose
- **THEN** its disposition is `Ungrounded`, it is excluded from `dnd_entities`, and its canonical record is retained for audit

### Requirement: Declined candidates are recorded, not silently dropped

When the model selects the `none` branch, the candidate SHALL be recorded with disposition `Declined` and its `reason`, so false-declines (real entities wrongly refused) are auditable. Declines SHALL NOT be silently discarded.

#### Scenario: Decline is logged with a reason

- **WHEN** the model returns `{"entityType":"none","reason":"..."}` for a candidate
- **THEN** the candidate is recorded as `Declined` with that reason and is retrievable for audit

### Requirement: Disposition is derived from grounding plus signals, not name and confidence alone

The disposition SHALL be derived from the combination of the grounding-gate result, the model's branch/decline choice, and the existing name/confidence heuristics — NOT from name + self-reported confidence alone. A confident, well-cased, but ungrounded extraction SHALL NOT receive `Accepted`.

#### Scenario: Confident but ungrounded does not pass

- **WHEN** the model reports high confidence and a clean name, but the grounding gate cannot ground the fields
- **THEN** the disposition is `NeedsReview`, not `Accepted`

#### Scenario: Backward compatibility for already-reviewed data

- **WHEN** an existing canonical entity has no `disposition` field (pre-change data)
- **THEN** it is treated as `Accepted` so prior human-reviewed output is not regressed

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

