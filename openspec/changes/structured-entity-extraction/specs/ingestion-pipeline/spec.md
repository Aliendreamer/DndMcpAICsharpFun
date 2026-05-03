## ADDED Requirements

### Requirement: The ingestion queue worker SHALL dispatch entity-extraction and entity-ingestion work items

The system SHALL extend `IngestionWorkItem` with two new types: `ExtractEntities` and `IngestEntities`. The `IngestionQueueWorker` SHALL dispatch each type to its respective orchestrator: `IEntityExtractionOrchestrator.ExtractEntitiesAsync` and `IEntityIngestionOrchestrator.IngestEntitiesAsync`. The existing `IngestBlocks` dispatch behaviour SHALL be unchanged.

#### Scenario: ExtractEntities item is dispatched to the entity extractor
- **WHEN** an `IngestionWorkItem(ExtractEntities, BookId: 5)` is dequeued
- **THEN** the worker invokes `IEntityExtractionOrchestrator.ExtractEntitiesAsync(5, ...)` and does not invoke any block-related orchestrator

#### Scenario: IngestEntities item is dispatched to the entity ingestor
- **WHEN** an `IngestionWorkItem(IngestEntities, BookId: 5)` is dequeued
- **THEN** the worker invokes `IEntityIngestionOrchestrator.IngestEntitiesAsync(5, ...)` and does not invoke any block-related orchestrator

#### Scenario: Existing IngestBlocks dispatch is unchanged
- **WHEN** an `IngestionWorkItem(IngestBlocks, BookId: 5)` is dequeued after this change
- **THEN** the worker still invokes `IBlockIngestionOrchestrator.IngestBlocksAsync(5, ...)`

### Requirement: Ingestion record status SHALL track entity-extraction and entity-ingestion progress

The system SHALL extend `IngestionStatus` (or add a parallel status track on `IngestionRecord`) with values that distinguish entity-extraction and entity-ingestion phases from block-ingestion phases (e.g. `EntitiesExtracting`, `EntitiesExtracted`, `EntitiesIngesting`, `EntitiesIngested`, `EntitiesFailed`). Block-ingestion statuses SHALL remain unchanged.

#### Scenario: Successful extraction transitions to EntitiesExtracted
- **WHEN** an entity extraction job completes successfully
- **THEN** the record's status (or its entity-extraction sub-status) becomes `EntitiesExtracted` and the canonical JSON path is recorded on the record

#### Scenario: Failed extraction transitions to EntitiesFailed
- **WHEN** an entity extraction job fails
- **THEN** the record's status becomes `EntitiesFailed` and an error message is persisted on the record

### Requirement: Book deletion SHALL clean up entity-collection points and the canonical JSON

`DELETE /admin/books/{id}` SHALL, in addition to its existing behaviour (delete block points, delete the PDF, delete the SQLite row), delete the entity points associated with the book from `dnd_entities` and delete the `data/canonical/<book-slug>.json` file from disk if it exists.

#### Scenario: Deletion removes block points, entity points, PDF, JSON, and record
- **WHEN** `DELETE /admin/books/{id}` is called for a book that has been block-ingested AND entity-ingested
- **THEN** points associated with the book's hash are removed from both `dnd_blocks` and `dnd_entities`, the PDF file is deleted, the canonical JSON is deleted, and the SQLite row is removed

#### Scenario: Deletion of book without entity ingestion is unaffected
- **WHEN** `DELETE /admin/books/{id}` is called for a book that was only block-ingested
- **THEN** block points are deleted, the PDF and SQLite row are deleted, and the entity-deletion attempt is a no-op (no error)
