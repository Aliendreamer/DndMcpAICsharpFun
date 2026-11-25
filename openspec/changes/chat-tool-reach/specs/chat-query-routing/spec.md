## ADDED Requirements

### Requirement: An empty reply SHALL be asked again with every tool

When the model's reply has no text, the system SHALL ask once more with the full tool set, and SHALL return a fixed message instead of an empty reply when the second reply is empty too.

#### Scenario: A hidden tool was called

- **WHEN** the first reply is empty because the model called a tool that was not offered
- **THEN** the turn is asked again with every tool and the second reply is returned

#### Scenario: Both replies are empty

- **WHEN** the second reply is empty too
- **THEN** a fixed message asks the user to rephrase

### Requirement: Chat SHALL offer one text search

Chat SHALL offer `search_dnd`, which searches prose and entities together, and SHALL NOT offer the prose-only `search_lore`. `search_lore` SHALL stay available to external MCP clients.

#### Scenario: A lore question in chat

- **WHEN** the chat tools are built
- **THEN** `search_dnd` is offered and `search_lore` is not

## MODIFIED Requirements

### Requirement: The router SHALL never strand the model — safe full-set fallback

Every narrowed tool set SHALL include an always-safe core: `search_dnd`, `search_entities`, `get_entity`, `calculate_crafting` and `ask_rules`, the tools the persona names for any question. When confidence is below the threshold, the classification is empty, or a tool is not in the group map, the system SHALL offer the full tool set (the pre-router behavior). The router SHALL only remove redundant tools when confident and SHALL never remove the always-safe core.

#### Scenario: Low confidence falls back to the full tool set
- **WHEN** the classifier's confidence is below the configured threshold
- **THEN** the full tool set is offered (identical to the behavior before this change)

#### Scenario: The always-safe core is always present in a narrowed set
- **WHEN** any group is offered under confidence
- **THEN** the offered set still includes every tool of the always-safe core

#### Scenario: An unmapped tool is always offered
- **WHEN** a registered chat tool is not present in the tool-group map
- **THEN** it is treated as always-offered and is never hidden by narrowing
