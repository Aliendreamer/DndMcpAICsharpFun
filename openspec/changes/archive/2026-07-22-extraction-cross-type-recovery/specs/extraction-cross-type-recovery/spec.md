## ADDED Requirements

### Requirement: A decline-bound candidate with a mundane-item signature SHALL be admitted as an Item, before the Rule rescue

The extraction pipeline SHALL offer the `Item` branch to a decline-bound candidate (one the `DeterministicTypeResolver` would decline) when it carries a SPECIFIC mundane-item stat signature — a weapon damage-type token (e.g. `1d8 slashing`) or an armor stat line (an AC figure paired with a `gp`/`sp` cost) — and this check SHALL run BEFORE the Rule rescue so a genuine item is never mis-typed as `Rule`. The item rescue rebinds `TypePrior = [EntityType.Item]` (the failed gated type is not re-offered); an `Item` pick is `Accepted` (non-gated, grounded by prose). A candidate WITHOUT the mundane-item signature SHALL NOT be item-rescued — in particular a rules passage falls through to the Rule rescue.

#### Scenario: A weapon/armor-stat candidate is admitted as Item

- **WHEN** a decline-bound candidate carries a mundane weapon-damage token or an armor AC+cost stat line
- **THEN** it is offered the `Item` branch (Item-or-none) and an `Item` pick is `Accepted`, not declined and not typed `Rule`

#### Scenario: A rule is never item-rescued

- **WHEN** a decline-bound candidate is a rules passage (e.g. "Switching Weapons") with no damage-type/armor-stat line
- **THEN** the item signature is false, it is NOT item-rescued, and it falls through to the Rule rescue (typed `Rule`, not `Item`)

#### Scenario: A real entity is never item-rescued

- **WHEN** a candidate resolves to a real entity type (not a Decline)
- **THEN** the item rescue does not run for it (the decline-only gate), and its extraction is unchanged
