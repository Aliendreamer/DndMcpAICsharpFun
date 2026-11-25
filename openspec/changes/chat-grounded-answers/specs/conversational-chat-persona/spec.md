## MODIFIED Requirements

### Requirement: Default companion persona produces conversational, grounded answers

The shipped `companion` persona SHALL instruct the model to: speak as a warm, knowledgeable D&D 5e companion in natural conversational prose; avoid reflexive bulleted "option" lists (using lists only when enumerating/comparing or when clarity demands); ground rules and lore using the retrieval tools (preferring `search_dnd` when available) cite the source book when relevant, and a page only when the result used carries one, never from memory; and never fabricate rules — stating when retrieval finds nothing rather than inventing content.

#### Scenario: Persona file encodes the conversational + grounding directives

- **WHEN** the `companion` persona is loaded
- **THEN** its text SHALL direct conversational prose over default bulleted lists, retrieval-grounded answers preferring `search_dnd`, source citation, and no fabricated rules
