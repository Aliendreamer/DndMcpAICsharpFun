## ADDED Requirements

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

## MODIFIED Requirements

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

## REMOVED Requirements

### Requirement: search_lore tool performs prose RAG search

**Reason**: its edition and category filters only hid results; every indexed book is 2014.

**Migration**: call `search_lore` with `query` and `topK`.
