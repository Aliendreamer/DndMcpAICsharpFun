## MODIFIED Requirements

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
