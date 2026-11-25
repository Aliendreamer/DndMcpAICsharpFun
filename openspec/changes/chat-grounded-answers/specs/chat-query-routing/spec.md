## ADDED Requirements

### Requirement: A question answered without a tool SHALL be looked up

When the model replies to a question without calling any tool, the chat SHALL run `search_entities` and, when available, `ask_rules` on the question itself, SHALL add their results to the conversation as tool results, and SHALL ask the model again without the first reply. The lookups SHALL be in the turn's tool trace, marked as made by the app. A message that is not a question SHALL NOT be looked up. A failed lookup SHALL NOT fail the turn.

#### Scenario: A rules question answered from memory

- **WHEN** the model answers "what DC is the concentration save?" without a tool call
- **THEN** the chat runs both lookups, asks again with their results and returns the second reply

#### Scenario: The model called a tool itself

- **WHEN** the model's reply comes after a tool call
- **THEN** the reply is returned as it is and the chat makes no lookup

#### Scenario: Not a question

- **WHEN** the user writes "thanks, great session"
- **THEN** the reply is returned as it is and the chat makes no lookup

#### Scenario: A lookup fails

- **WHEN** every lookup throws
- **THEN** the first reply is returned
