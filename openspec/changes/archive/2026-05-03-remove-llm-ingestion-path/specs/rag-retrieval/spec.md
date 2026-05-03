# rag-retrieval (delta)

## REMOVED Requirements

### Requirement: Retrieval queries the collection named by Retrieval:Collection
**Reason**: There is now only one Qdrant collection (`dnd_blocks`). The runtime collection-selection knob is no longer meaningful.
**Migration**: Remove `Retrieval:Collection` from `appsettings.json`, `appsettings.Development.json`, environment variables, and any compose-file overrides. The setting is silently ignored after this change.

## MODIFIED Requirements

### Requirement: Public and admin retrieval endpoints query the blocks collection
The system SHALL expose `GET /retrieval/search` (public) and `GET /admin/retrieval/search` (admin, includes scores and Qdrant point IDs) and route every query to the Qdrant collection named by `Qdrant:BlocksCollectionName`. The query parameter shape and response JSON shape are unchanged from before this change.

#### Scenario: Search returns results from the blocks collection
- **WHEN** `GET /retrieval/search?q=fireball&topK=5` is issued against a deployment whose `dnd_blocks` collection has been populated
- **THEN** the response is an HTTP 200 list of result objects whose `text` and `metadata` are sourced from `dnd_blocks` points

#### Scenario: Search returns an empty list when the collection is empty
- **WHEN** the same query is issued against an empty `dnd_blocks` collection
- **THEN** the response is an HTTP 200 empty list, not an error
