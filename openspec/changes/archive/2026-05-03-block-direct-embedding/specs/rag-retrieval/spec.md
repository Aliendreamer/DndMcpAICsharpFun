# rag-retrieval (delta)

## ADDED Requirements

### Requirement: Retrieval queries the collection named by Retrieval:Collection
The system SHALL read the configuration value `Retrieval:Collection` (`"chunks"` or `"blocks"`, default `"chunks"`) at startup and route every `/retrieval/search` and `/admin/retrieval/search` query to the corresponding Qdrant collection. Query semantics, parameters, and response shape SHALL be identical regardless of which collection is selected — only the underlying point source changes.

#### Scenario: Default deployment hits the chunks collection
- **WHEN** the application starts with `Retrieval:Collection` unset or set to `"chunks"`
- **THEN** `/retrieval/search?q=fireball` returns matches from `dnd_chunks` and not from `dnd_blocks`

#### Scenario: Switching the flag points retrieval at the blocks collection
- **WHEN** the application starts with `Retrieval:Collection=blocks`
- **THEN** `/retrieval/search?q=fireball` returns matches from `dnd_blocks` and not from `dnd_chunks`

#### Scenario: Invalid value falls back to chunks
- **WHEN** the configuration value is something other than `"chunks"` or `"blocks"`
- **THEN** the service logs a warning and uses `dnd_chunks` to avoid silently routing traffic to an unintended collection

#### Scenario: Response shape is unchanged
- **WHEN** the same query is run against `chunks` and `blocks` (with both collections populated)
- **THEN** the response JSON has the same fields and types — only the result content differs
