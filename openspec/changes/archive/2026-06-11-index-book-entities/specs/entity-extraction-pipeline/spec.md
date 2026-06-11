## ADDED Requirements

### Requirement: Force re-extraction overrides any prior ingestion status
The system SHALL allow `POST /admin/books/{id}/extract-entities?force=true` to proceed regardless of the book's current `IngestionStatus`, including a stuck `EntitiesExtracting` or `EntitiesIngesting` left by an interrupted run. When `force=true`, the pipeline SHALL overwrite any existing canonical JSON for the book and run extraction to completion (or to a resumable checkpoint).

#### Scenario: Force overrides a stuck EntitiesExtracting status
- **WHEN** a book is left in `EntitiesExtracting` with no active run and `extract-entities?force=true` is called
- **THEN** extraction runs and produces `books/canonical/<slug>.json`, and the book's status advances past `EntitiesExtracting`

#### Scenario: Force overwrites existing canonical JSON
- **WHEN** `extract-entities?force=true` is called for a book that already has canonical JSON
- **THEN** the canonical JSON is regenerated and replaced

#### Scenario: Extracted entities become searchable after ingestion
- **WHEN** a book's canonical JSON is ingested via `POST /admin/books/{id}/ingest-entities`
- **THEN** `dnd_entities` contains the book's entity records and entity search returns results for them
