## ADDED Requirements

### Requirement: A system-prompt persona is injected into each chat request

`DndChatService` SHALL prepend exactly one `ChatRole.System` message — the active persona text — to the messages sent to the chat model on every request. This system message SHALL NOT be added to the in-memory `History` and SHALL NOT be persisted as a conversation turn.

#### Scenario: System persona is prepended for the request

- **WHEN** a user message is sent
- **THEN** the messages passed to the chat model SHALL begin with one `ChatRole.System` message equal to the active persona text, followed by the conversation history

#### Scenario: Persona is never persisted or stored in history

- **WHEN** a user message is sent and the assistant replies
- **THEN** `History` SHALL contain only user and assistant messages (no system role)
- **THEN** only the user and assistant turns SHALL be persisted (the persona SHALL NOT be persisted)

### Requirement: Persona is selected by configuration with a safe fallback

The active persona SHALL be selected by the `Chat:Persona` configuration value (default `companion`), loading `Config/personas/<persona>.md`. If the configured persona file is missing or empty, the system SHALL fall back to a built-in default persona so chat never runs without a system prompt.

#### Scenario: Default persona when unset

- **WHEN** `Chat:Persona` is not configured
- **THEN** the `companion` persona file SHALL be loaded as the active persona

#### Scenario: Configured persona is loaded

- **WHEN** `Chat:Persona` is set to `dm` and `Config/personas/dm.md` exists
- **THEN** the active persona text SHALL be the contents of `dm.md`

#### Scenario: Fallback when file missing

- **WHEN** `Chat:Persona` names a persona whose file does not exist
- **THEN** the built-in default persona SHALL be used and chat SHALL still function

### Requirement: Default companion persona produces conversational, grounded answers

The shipped `companion` persona SHALL instruct the model to: speak as a warm, knowledgeable D&D 5e companion in natural conversational prose; avoid reflexive bulleted "option" lists (using lists only when enumerating/comparing or when clarity demands); ground rules and lore using the retrieval tools (preferring `search_dnd` when available) and cite the source book/page when relevant; and never fabricate rules — stating when retrieval finds nothing rather than inventing content.

#### Scenario: Persona file encodes the conversational + grounding directives

- **WHEN** the `companion` persona is loaded
- **THEN** its text SHALL direct conversational prose over default bulleted lists, retrieval-grounded answers preferring `search_dnd`, source citation, and no fabricated rules
