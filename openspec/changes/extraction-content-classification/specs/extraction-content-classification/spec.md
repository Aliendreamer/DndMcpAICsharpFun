## ADDED Requirements

### Requirement: Non-entity content SHALL be classified to its category, not just declined
The extraction classifier SHALL map a scanned candidate to one of: a typed entity, a recognized non-entity category (at least Rule, Lore, Table, Variant/Sidebar), or true structural noise. Only true noise (TOC, chapter/section headers with no content, fragments) SHALL be declined; recognized non-entity content SHALL be classified and kept with its category and provenance. Classifications SHALL remain grounded (ungrounded content is rejected, never fabricated).

#### Scenario: A rule is classified as Rule, not declined
- **WHEN** a candidate like "Switching Weapons" is a rule, not a monster/entity
- **THEN** it is classified as `Rule` (kept, with provenance), not declined as `none`

#### Scenario: A lore passage is classified as Lore
- **WHEN** a deity-pantheon / setting passage is not a discrete entity
- **THEN** it is classified as `Lore`, not declined

#### Scenario: True noise is still declined
- **WHEN** a candidate is a table-of-contents entry or bare chapter header with no content
- **THEN** it is declined as structural noise

#### Scenario: The taxonomy is shared with retrieval
- **WHEN** a candidate is classified into a non-entity category
- **THEN** the category vocabulary aligns with the block-level `ContentCategory` used by `ask_rules`/`ask_setting_lore` (one taxonomy, not two)
