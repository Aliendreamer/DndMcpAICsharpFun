## ADDED Requirements

### Requirement: Decline-bound rule content SHALL be admittable as a Rule entity

The extraction pipeline SHALL offer the `Rule` branch (a first-class non-gated `EntityType.Rule`, with a minimal `RuleFields` schema) to a candidate ONLY when that candidate is decline-bound — the `DeterministicTypeResolver` would decline it (gated prior + no 5etools match) or it has no non-gated entity option — AND it carries a rule signature (substantial prose, non-entity-name heading, not a fragment/TOC). The LLM then selects `Rule` or `none`. A candidate that resolves to a real entity type SHALL NOT be offered the `Rule` branch. A selected `Rule` SHALL be `Accepted` (grounded by its own book prose, no 5etools name-match required) and flow into `dnd_entities` like any entity, its prose in `canonicalText` and its category aligning with block `ContentCategory.Rule`.

#### Scenario: A declined rule is admitted as Rule

- **WHEN** a decline-bound candidate with a rule signature (e.g. a DMG rules passage that today declines as `none`) is extracted
- **THEN** the union offers the `Rule` branch, the LLM may select `Rule`, and the result is an `Accepted` `EntityType.Rule` entity (not a decline)

#### Scenario: A real entity is never offered Rule

- **WHEN** a candidate resolves to a real entity type (e.g. a Spell with a 5etools match, or a complete monster stat block)
- **THEN** the `Rule` branch is NOT offered for it and its extraction is unchanged

#### Scenario: True noise is still declined, not turned into a Rule

- **WHEN** a candidate is a fragment / table-of-contents entry / bare chapter header
- **THEN** the existing structural filters reject it before Rule is offered, so it is declined as noise and never becomes a `Rule` entity

#### Scenario: Rule shares the retrieval taxonomy

- **WHEN** content is classified as `EntityType.Rule`
- **THEN** that category aligns with the block-level `ContentCategory.Rule` used by `ask_rules` (one vocabulary, not two)
