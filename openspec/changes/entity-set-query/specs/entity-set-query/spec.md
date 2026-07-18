## ADDED Requirements

### Requirement: A filter query SHALL return the complete matching set, not a similarity-ranked top-K

The system SHALL provide a filter-only entity retrieval path that returns every entity matching the given structured filters (type, CR range, spell level, damage type, keyword, source book, SRD flags), up to a configured cap. Retrieval SHALL NOT rank by vector similarity and SHALL NOT require a query string. It reuses the existing entity payload filter and the Qdrant scroll/count primitives.

#### Scenario: All matches of a filter are returned (up to the cap)
- **WHEN** the client requests the set for `type=Monster, crGte=5, crLte=5, keyword=Flying`
- **THEN** the result contains every matching entity up to the cap, ordered by scroll order (not by similarity to any query string)

#### Scenario: A filter with no query string still works
- **WHEN** a set query supplies only structured filters and no natural-language query
- **THEN** the complete matching set is returned (unlike semantic search, which requires a query)

### Requirement: Set results SHALL be compact and report an honest total

Each result row SHALL carry only identifying/discriminating fields — id, name, type, source book, page, and the present discriminators (CR, spell level, damage type) — and SHALL NOT include the entity's canonical text or full fields. The result SHALL report the true total match count and the returned count; when the total exceeds the returned count the response SHALL make the truncation explicit rather than silently dropping matches.

#### Scenario: Rows are compact, not full entities
- **WHEN** a set query returns rows
- **THEN** each row is a compact summary (id/name/type/source/page + discriminators) with no canonical text, so the caller drills into a specific entity via `get_entity`

#### Scenario: Truncation is explicit
- **WHEN** 137 entities match but the cap is 50
- **THEN** the result reports total 137 and returned 50 and signals that the set is truncated (e.g. advises narrowing the filter) — it never silently returns 50 as if complete

### Requirement: The set path SHALL be exposed as a distinct tool and endpoint

The system SHALL expose the complete-set path as a `list_entities` MCP tool (distinct from the semantic `search_entities`, which is unchanged) and as a public `GET /retrieval/entities/list` endpoint with the same rate limiting as the existing entity search. The `list_entities` tool SHALL be registered in the query router's `structured-lookup` group. The `.http` and `.insomnia` API references SHALL include the new endpoint.

#### Scenario: list_entities returns a complete set while search_entities is unchanged
- **WHEN** the LLM calls `list_entities` with structured filters
- **THEN** it receives `{ total, returned, rows }`, and `search_entities` continues to return semantic top-K results with no behavior change

#### Scenario: The set tool routes with the structured-lookup group
- **WHEN** the query router classifies a set query ("all…"/"every…"/"how many…") to `structured-lookup`
- **THEN** `list_entities` is among the offered tools for that group
