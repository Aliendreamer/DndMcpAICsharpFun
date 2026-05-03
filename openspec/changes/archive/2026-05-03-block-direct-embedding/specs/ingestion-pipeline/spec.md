# ingestion-pipeline (delta)

## ADDED Requirements

### Requirement: A new IngestionWorkType drives block ingestion
The `IngestionWorkType` enum SHALL gain a new member `IngestBlocks`. The `IngestionQueueWorker` SHALL dispatch work items of this type to a block-ingestion handler that runs the no-LLM path described in `block-ingestion`. A book SHALL never have two work items of any type simultaneously in `Processing` status.

#### Scenario: A queue worker dispatches IngestBlocks items correctly
- **WHEN** an `IngestionWorkItem(Type: IngestBlocks, BookId: 5)` is dequeued
- **THEN** the worker invokes the block-ingestion path for book 5 and not the LLM-extraction path

#### Scenario: Concurrent extract and block-ingest are serialised
- **WHEN** `/admin/books/5/ingest-blocks` is requested while a previous `/admin/books/5/extract` is still `Processing`
- **THEN** the second request returns HTTP 409 (already processing) without enqueuing
