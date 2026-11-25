# mcp-retrieval-tools Specification

## Purpose
TBD - created by archiving change mcp-retrieval-tools. Update Purpose after archive.

## Requirements

### Requirement: search_entities tool performs structured entity search
The server SHALL expose a `search_entities` MCP tool that queries `IEntityRetrievalService` and returns matching entity records.

Parameters:

- `query` (string, required) — search text
- `type` (string, optional) — entity type e.g. `"Spell"`, `"Monster"`, `"Class"`, `"Subclass"`
- `keyword` (string, optional) — trait tag keyword e.g. `"Amphibious"`, `"Pack Tactics"`
- `crMax` (double, optional) — maximum challenge rating (for monsters)
- `spellLevel` (int, optional) — spell level filter
- `srd` (bool, optional) — restrict to SRD 5.1 entities
- `srd52` (bool, optional) — restrict to SRD 5.2.1 entities
- `topK` (int, optional, default 10) — number of results

#### Scenario: Basic entity search returns results

- **WHEN** a client calls `search_entities` with a non-empty `query`
- **THEN** the tool returns up to `topK` entity records with id, name, type, sourceBook, edition, canonicalText, and fields

#### Scenario: search_entities with type filter narrows results

- **WHEN** a client calls `search_entities` with `query` and `type="Monster"`
- **THEN** only Monster entities are returned

#### Scenario: search_entities with crMax filter narrows monster results

- **WHEN** a client calls `search_entities` with `type="Monster"` and `crMax=2`
- **THEN** only monsters with CR ≤ 2 are returned

#### Scenario: search_entities with invalid type returns empty gracefully

- **WHEN** a client calls `search_entities` with an unrecognised `type` string
- **THEN** the tool returns an empty result list without throwing

### Requirement: get_entity tool fetches a single entity by ID
The server SHALL expose a `get_entity` MCP tool that retrieves one entity from `IEntityRetrievalService` by its canonical ID.

Parameters:

- `id` (string, required) — canonical entity ID e.g. `"phb.spell.fireball"`

#### Scenario: get_entity returns the entity for a known ID

- **WHEN** a client calls `get_entity` with a valid entity ID
- **THEN** the tool returns the full entity record

#### Scenario: get_entity returns not-found message for unknown ID

- **WHEN** a client calls `get_entity` with an ID that does not exist
- **THEN** the tool returns a clear "entity not found" message string rather than throwing

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

### Requirement: search_lore tool searches prose by query alone
The server SHALL expose a `search_lore` MCP tool that queries `IRagRetrievalService` and returns matching prose blocks.

Parameters:

- `query` (string, required) — natural language question
- `topK` (int, optional, default 5) — number of results

#### Scenario: Basic lore search returns results

- **WHEN** a client calls `search_lore` with a non-empty `query`
- **THEN** the tool returns up to `topK` prose blocks with title, text, sourceBook, category, and score

#### Scenario: No edition or category argument

- **WHEN** a client reads the tool's schema
- **THEN** it has no `version` and no `category` parameter, because every indexed book is 2014 and block categories are coarse
