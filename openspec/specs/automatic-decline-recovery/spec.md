# automatic-decline-recovery Specification

## Purpose
TBD - created by archiving change automatic-decline-recovery. Update Purpose after archive.
## Requirements
### Requirement: Still-declined candidates SHALL be automatically recovered as grounded Rule/Lore entities

After the main extraction loop for an official book, the pipeline SHALL run a recovery phase over the still-declined candidates (no separate trigger — inside `extract-entities`). For each, it SHALL issue one recovery-framed union call offering `[Rule, Lore]` and `none`, with a system prompt establishing the content as real, official, non-fabricated and asking for Rule (mechanical), Lore (worldbuilding/setting), or `none` (heading/TOC/fragment). A `Rule`/`Lore` selection that grounds via the existing grounding cascade SHALL be admitted as an entity (`dataSource:"decline-recovery"`, `authority:"canon-unindexed"`) and removed from the decline audit; a `none` selection or an ungrounded result SHALL remain declined. The main extraction gate SHALL be unchanged.

#### Scenario: A wrongly-declined mechanical rule is recovered as Rule

- **WHEN** a candidate like `psychic-wind-effects` was declined by the main extraction but is a real DMG mechanical rule
- **THEN** the recovery phase re-classifies it as `Rule`, and if its fields ground against its text it is admitted as a `Rule` entity and dropped from the decline audit

#### Scenario: Worldbuilding narrative is recovered as Lore

- **WHEN** a declined candidate is real setting/worldbuilding narrative (e.g. `loose-pantheons`)
- **THEN** the recovery phase may classify it as `Lore` and, if grounded, admit it as a `Lore` entity

#### Scenario: True noise stays declined, never fabricated

- **WHEN** a declined candidate is a pure chapter heading / TOC entry / fragment
- **THEN** the recovery selects `none` (or the result fails grounding) and it stays declined — the recovery never fabricates and never lowers the grounding bar

#### Scenario: Homebrew books skip recovery

- **WHEN** the book is not official (no fivetools source key)
- **THEN** the "real by definition" recovery phase is skipped (web-vouch recovery is out of scope)

#### Scenario: Recovery is automatic and additive

- **WHEN** `extract-entities` runs for an official book
- **THEN** the recovery phase runs automatically as the final step, with no separate endpoint or manual trigger, and the main extraction results are unchanged except for the added recovered entities

