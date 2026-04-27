## ADDED Requirements

### Requirement: Ingestion detects duplicate books by file hash
The system SHALL mark a newly registered book as `Duplicate` if another `Completed` record with the same SHA-256 hash already exists, without performing any embedding or vector store writes.

#### Scenario: Duplicate book is marked and skipped
- **WHEN** `IngestBookAsync` is called for a `Pending` record whose file hash matches an existing `Completed` record
- **THEN** the system sets the record status to `Duplicate` and stops without producing chunks or writing to Qdrant

## MODIFIED Requirements

### Requirement: The ingestion orchestrator processes a book end-to-end
The system SHALL compute the file hash as the first step of ingestion, persist it immediately, reuse it for duplicate detection and unchanged-file checks, then extract text, chunk, embed, upsert into the vector store, and mark the record `Completed`.

#### Scenario: Book is ingested successfully
- **WHEN** `IngestBookAsync` is called for a `Pending` record
- **THEN** the system computes and stores the hash, extracts pages, produces chunks, embeds and upserts them, and sets status to `Completed` with a chunk count

#### Scenario: Unchanged book is skipped
- **WHEN** `IngestBookAsync` is called for a `Completed` record whose file hash has not changed
- **THEN** the system skips processing and leaves the record unchanged

#### Scenario: Ingestion failure marks the record as Failed
- **WHEN** an unhandled exception occurs during ingestion
- **THEN** the system sets the record status to `Failed` and stores the error message
