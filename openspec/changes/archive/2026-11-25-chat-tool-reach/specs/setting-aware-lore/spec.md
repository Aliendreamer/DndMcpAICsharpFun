## MODIFIED Requirements

### Requirement: Per-campaign grounded, cited lore tool scoped to the setting

The system SHALL expose a per-user chat tool `ask_setting_lore(question, setting?, campaignId?)` that closes
over the authenticated session user id and never accepts a user id as a tool argument. It SHALL
verify that a given campaign belongs to the caller (a campaign that is not the caller's SHALL NOT be
read or revealed; the question is then answered from the books of the named setting alone, as when no
campaign is given), resolve the campaign's setting to a source-book scope, run prose retrieval RESTRICTED
to that scope, and return the retrieved passages each carrying its citation (source book and section
or title). When the setting's sources contain nothing relevant, it SHALL return an explicit empty
result rather than unscoped or fabricated lore. The tool's contract SHALL require the answer to be
composed only from the returned cited passages.

#### Scenario: Answer is scoped to the campaign's setting sources

- **WHEN** `ask_setting_lore` is called for an Eberron campaign with an Eberron lore question
- **THEN** the returned passages SHALL come only from the Eberron setting book or the core rulebooks, each with its citation

#### Scenario: Foreign campaign is rejected

- **WHEN** `ask_setting_lore` is called with a `campaignId` belonging to a different user
- **THEN** that campaign SHALL NOT be read, and the answer SHALL come from the named setting's books (or from every book when none is named)

#### Scenario: Empty when the setting's sources do not cover it

- **WHEN** the scoped retrieval finds nothing relevant in the setting's sources
- **THEN** the tool SHALL return an explicit empty result, not an unscoped or invented answer

#### Scenario: Tool schema does not expose a user id argument

- **WHEN** the `ask_setting_lore` tool schema is inspected
- **THEN** it SHALL NOT expose a `userId` argument (the caller identity comes from the session)

#### Scenario: No campaign given

- **WHEN** `ask_setting_lore` is called with a setting name and no campaign
- **THEN** the passages SHALL come from that setting's books and the core rulebooks
