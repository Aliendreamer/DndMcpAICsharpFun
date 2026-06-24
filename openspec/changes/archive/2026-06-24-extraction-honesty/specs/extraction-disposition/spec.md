## ADDED Requirements

### Requirement: Each extracted candidate carries a disposition

Every processed extraction candidate SHALL be assigned exactly one disposition: `Accepted` (typed, grounded, eligible for `dnd_entities`), `NeedsReview` (emitted but ungrounded, or low/medium confidence, or OCR-noisy name, or an ambiguous decline), `Declined` (the model chose `none` on clearly non-entity content), or `Failed` (no valid output after retries). The disposition SHALL be persisted on the canonical-JSON entity record (replacing the `needsReview` boolean as the trust signal).

#### Scenario: Grounded, confident entity is Accepted

- **WHEN** an entity is typed by the model, grounded by the gate, and not flagged by name/confidence
- **THEN** its disposition is `Accepted`

#### Scenario: Ungrounded entity is NeedsReview

- **WHEN** an entity is emitted but the grounding gate cannot ground its key fields
- **THEN** its disposition is `NeedsReview`

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
