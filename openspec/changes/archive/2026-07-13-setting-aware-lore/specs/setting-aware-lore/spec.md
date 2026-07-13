## ADDED Requirements

### Requirement: A campaign declares a setting resolved to source books

The system SHALL provide a setting catalog mapping each named setting to a set of source-book keys,
always unioned with the core rulebooks (Player's Handbook, Dungeon Master's Guide, Monster Manual).
The catalog SHALL include a generic/no-setting default whose resolved scope is empty (no source-book
restriction). A `Campaign` SHALL carry an optional setting value; an absent, unknown, or generic
setting SHALL resolve to the empty (unscoped) scope rather than an error.

#### Scenario: A named setting resolves to its books plus core

- **WHEN** a campaign's setting is Eberron
- **THEN** the resolved source-book scope SHALL contain the Eberron source book and the core rulebooks

#### Scenario: No setting resolves to unscoped

- **WHEN** a campaign has no setting (or a generic/unknown value)
- **THEN** the resolved scope SHALL impose no source-book restriction

#### Scenario: Campaign setting persists

- **WHEN** a campaign is created or updated with a setting
- **THEN** the setting SHALL be persisted and returned on subsequent reads

### Requirement: Setting selectable on the campaign form

The system SHALL let a user choose a campaign's setting from the catalog on the campaign create/edit
form, and persist the selection with the campaign.

#### Scenario: DM sets the campaign setting

- **WHEN** a user selects a setting on the campaign form and saves
- **THEN** the campaign SHALL persist that setting and the form SHALL reflect it on reload

### Requirement: Per-campaign grounded, cited lore tool scoped to the setting

The system SHALL expose a per-user chat tool `ask_setting_lore(campaignId, question)` that closes
over the authenticated session user id and never accepts a user id as a tool argument. It SHALL
verify the campaign belongs to the caller (rejecting a campaign that is not the caller's without
revealing it), resolve the campaign's setting to a source-book scope, run prose retrieval RESTRICTED
to that scope, and return the retrieved passages each carrying its citation (source book and section
or title). When the setting's sources contain nothing relevant, it SHALL return an explicit empty
result rather than unscoped or fabricated lore. The tool's contract SHALL require the answer to be
composed only from the returned cited passages.

#### Scenario: Answer is scoped to the campaign's setting sources

- **WHEN** `ask_setting_lore` is called for an Eberron campaign with an Eberron lore question
- **THEN** the returned passages SHALL come only from the Eberron setting book or the core rulebooks, each with its citation

#### Scenario: Foreign campaign is rejected

- **WHEN** `ask_setting_lore` is called with a `campaignId` belonging to a different user
- **THEN** the call SHALL be rejected and SHALL NOT reveal that campaign's setting or lore

#### Scenario: Empty when the setting's sources do not cover it

- **WHEN** the scoped retrieval finds nothing relevant in the setting's sources
- **THEN** the tool SHALL return an explicit empty result, not an unscoped or invented answer

#### Scenario: Tool schema does not expose a user id argument

- **WHEN** the `ask_setting_lore` tool schema is inspected
- **THEN** it SHALL NOT expose a `userId` argument (the caller identity comes from the session)
