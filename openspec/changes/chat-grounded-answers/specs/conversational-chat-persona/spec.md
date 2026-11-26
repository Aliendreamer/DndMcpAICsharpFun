## MODIFIED Requirements

### Requirement: Default companion persona produces conversational, grounded answers
The shipped `companion` persona SHALL be short, and SHALL open (after one line naming the role) with the rule that any question about D&D rules, spells, monsters, classes, items or lore starts with a tool call and is answered only from what the tools return. A long persona made qwen3:8b treat questions as writing tasks and answer from memory: in a probe of 8 reference questions it called a tool 3 times with the old 870-word persona and 8 times with a short one. The persona SHALL also direct conversational prose over default lists, citing the source book, a page only when a result carries one and never from memory, and saying so when the tools find nothing instead of inventing content.

#### Scenario: Persona file encodes the conversational + grounding directives

- **WHEN** the `companion` persona is loaded
- **THEN** its first rule SHALL require a tool call before answering a D&D question, and its text SHALL direct conversational prose, source citation with pages only from results, and no fabricated rules
