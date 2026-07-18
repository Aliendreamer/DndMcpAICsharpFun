## ADDED Requirements

### Requirement: A mis-typed gated-prior candidate SHALL be admitted under its correct type when signalled

When a gated-prior candidate carries clear signals for a different *real* entity type than its (mis-derived) prior — e.g. armor/weapon/item markers on a `Monster`-prior candidate — the extraction SHALL admit it under the correct type rather than decline it, provided its fields ground against the source. The anti-fabrication guarantee is preserved: `none` remains offered and the grounding cascade still rejects ungrounded fields, so a re-type never fabricates.

#### Scenario: An armor item mis-prior'd as Monster is admitted as an Item
- **WHEN** a candidate like "Light Armor" / a specific armor is derived with a `Monster` prior but its text carries item/armor signals
- **THEN** it is admitted as an `Item` (not declined as "not a monster"), and its fields are grounded by the cascade

#### Scenario: Genuine noise is still declined
- **WHEN** a mis-prior'd candidate has no positive signal for any real type (a chapter heading, TOC entry)
- **THEN** it is still declined — the recovery re-types real content, it does not lower the noise bar

### Requirement: The rules-content policy SHALL be explicit and consistent

The system SHALL apply one explicit policy for rules-section content (e.g. Spellcasting, multiclassing procedures): either kept as prose-only (answered via `ask_rules` over `dnd_blocks`) or admitted as `Rule` entities. The chosen policy SHALL be applied consistently in the decline gate so identical rules content is not sometimes declined and sometimes kept.

#### Scenario: Rules content is handled by the chosen policy
- **WHEN** a rules-section candidate (e.g. "Spellcasting") is extracted
- **THEN** it is handled per the documented policy — declined-to-prose OR admitted as a `Rule` entity — consistently, not ad hoc

#### Scenario: The decline audit records the reason
- **WHEN** a rules candidate is declined under the prose-only policy
- **THEN** the decline is recorded in `.declined.json` with a reason, so the choice is auditable
