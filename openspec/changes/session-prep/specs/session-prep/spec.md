## ADDED Requirements

### Requirement: Session prep composes a campaign-scoped grounded packet

The system SHALL compose a session-prep packet for a caller's own campaign by combining the shipped
encounter, NPC, and setting-lore surfaces: an encounter built for the campaign's party (themed, at the
requested difficulty), an NPC generated for a caller-chosen archetype, and setting lore hooks derived
from the theme and scoped to the campaign's setting. Each sub-result SHALL retain its own grounding
(the encounter's math, the NPC's real stat block or re-pick roster, the cited lore passages). The
campaign ownership check SHALL be enforced before any prep work such that a campaign that is not the
caller's is rejected.

#### Scenario: Prep returns the three grounded pieces for an owned campaign

- **WHEN** session prep is requested for a campaign the caller owns, with a theme and an NPC archetype
- **THEN** it SHALL return a packet containing the built encounter, the generated NPC, and the setting lore hooks, each carrying its own grounded result

#### Scenario: A foreign campaign is rejected before any prep

- **WHEN** session prep is requested for a campaign that belongs to a different user
- **THEN** the request SHALL be rejected (unauthorized) and SHALL NOT compose a packet or reveal that campaign's party or setting

#### Scenario: Lore hooks are derived from the theme and setting-scoped

- **WHEN** the packet's lore hooks are produced
- **THEN** they SHALL come from a question derived from the theme, scoped to the campaign's setting; a campaign with no setting SHALL yield unscoped hooks (drawn from the corpus at large, not restricted to a setting) while the encounter and NPC are still returned

### Requirement: Grounded session-prep tool

The system SHALL expose a per-session chat tool `prep_session(campaignId, theme, difficulty?,
npcArchetype)` that closes over the authenticated session user id and does not accept a user id as an
argument. Its contract SHALL require the session outline to be composed from the returned grounded
packet (the encounter, the NPC's stat block, the cited lore hooks), citing each, and to re-pick the
NPC archetype from the returned available archetypes when the chosen one is not in the corpus.

#### Scenario: Tool returns the grounded packet for the persona to compose

- **WHEN** `prep_session` is called for the caller's campaign with a theme and archetype
- **THEN** it SHALL return the composed packet, and the contract SHALL require the outline to be built from those grounded pieces (encounter, NPC stats, cited hooks)

#### Scenario: Tool schema does not expose a user id

- **WHEN** the `prep_session` tool schema is inspected
- **THEN** it SHALL NOT expose a `userId` argument
