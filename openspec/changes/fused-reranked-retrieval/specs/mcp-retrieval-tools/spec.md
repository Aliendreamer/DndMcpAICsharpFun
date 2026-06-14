## ADDED Requirements

### Requirement: search_dnd tool performs fused cross-channel retrieval

The MCP server SHALL expose a `search_dnd` tool that takes a `query` and `topK` and returns a single ranked list combining prose passages (`dnd_blocks`) and structured entities (`dnd_entities`), jointly reranked by the cross-encoder. Each result SHALL include a `source` field (`prose` or `entity`), an identifier, and a snippet/title. The existing `search_lore`, `search_entities`, and `get_entity` tools SHALL remain available and unchanged.

#### Scenario: search_dnd returns a merged ranked list

- **WHEN** `search_dnd` is called with a query and `topK`
- **THEN** it SHALL return up to `topK` results drawn from both prose and entities, ordered by a single reranking over the combined candidates

#### Scenario: Each result identifies its source

- **WHEN** `search_dnd` returns a result sourced from `dnd_entities`
- **THEN** that result SHALL have `source: "entity"` and the entity id

#### Scenario: Existing tools are unaffected

- **WHEN** `search_lore`, `search_entities`, or `get_entity` are called after `search_dnd` is added
- **THEN** they SHALL behave as before
