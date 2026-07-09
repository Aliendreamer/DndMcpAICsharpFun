## MODIFIED Requirements

### Requirement: Grounding is a tiered cascade that escalates only the residual

The grounding gate SHALL be a tiered cascade run cheap-first, implemented as the shared
`GroundingCascade`: Tier 0 — OCR-normalized fuzzy match per field; Tier 1 — embedding grounding
reusing the existing `dnd_blocks`/`mxbai-embed-large` vectors, **scoped to the entity's own
`SourceBook` and to pages within a configured window of the entity's `Page`**; Tier 2 — an LLM
judge (`qwen3:8b`) run ONLY on candidates the cheaper tiers cannot resolve. The cheaper tiers SHALL
short-circuit grounding for entities they can confirm or reject, so the expensive judge runs on a
bounded residual. **Tier 1 SHALL be an escalation gate only: topical embedding similarity alone
SHALL NEVER mark an entity `Grounded`. A similarity below the configured floor SHALL reject the
entity (`Ungrounded`); a similarity at or above the floor without a Tier-0 field confirmation SHALL
escalate to the Tier 2 judge rather than ground the entity.**

#### Scenario: Cheap tier resolves without invoking the judge

- **WHEN** Tier 0/Tier 1 confidently ground (or reject) an entity's fields
- **THEN** the Tier 2 LLM judge is NOT invoked for that entity

#### Scenario: Only the residual escalates to the judge

- **WHEN** an entity's grounding is unresolved after Tier 0/Tier 1
- **THEN** it is escalated to the Tier 2 judge, and the count of escalated candidates is recorded

#### Scenario: Topical similarity escalates instead of grounding

- **WHEN** an entity's Tier 1 similarity is at or above the floor but no field was Tier-0 confirmed
- **THEN** Tier 1 SHALL NOT mark it `Grounded`; it SHALL be escalated to the Tier 2 judge
