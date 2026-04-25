## ADDED Requirements

### Requirement: Ingestion records are persisted in SQLite
The system SHALL maintain an `ingestion_records` table in SQLite with fields: `id`, `file_path`, `file_name`, `file_hash` (SHA256), `source_name`, `version`, `display_name`, `status`, `error`, `chunk_count`, `ingested_at`, `created_at`.

#### Scenario: Record is created on book registration
- **WHEN** a book is registered via `POST /admin/books/register`
- **THEN** a row is inserted into `ingestion_records` with `status = Pending`

#### Scenario: Schema is applied automatically on startup
- **WHEN** the application starts for the first time
- **THEN** EF Core migrations run and the `ingestion_records` table is created

### Requirement: SHA256 hash prevents duplicate ingestion
The system SHALL compute the SHA256 hash of the PDF file bytes before ingestion and skip processing if a `Completed` record with that hash already exists.

#### Scenario: File with known hash is skipped
- **WHEN** the background service processes a file whose SHA256 matches an existing `Completed` record
- **THEN** the file is skipped and no new chunks are produced

#### Scenario: Renamed file with known hash is still skipped
- **WHEN** a file is renamed but its contents are unchanged
- **THEN** the background service recognises the hash match and skips it

### Requirement: Failed ingestions are retried on next cycle
The system SHALL set a record's status to `Failed` with an error message when ingestion throws an unhandled exception, and SHALL retry `Failed` records on the next background service cycle.

#### Scenario: Exception during ingestion marks record as Failed
- **WHEN** an exception occurs during PDF extraction or chunking
- **THEN** the record status is set to `Failed` and the exception message is stored in the `error` field

#### Scenario: Failed record is retried on next cycle
- **WHEN** the background service runs its next 24h cycle
- **THEN** records with `status = Failed` are reprocessed

### Requirement: Completed ingestion records the chunk count
The system SHALL update `chunk_count` and `ingested_at` on a record when ingestion completes successfully.

#### Scenario: Successful ingestion updates record
- **WHEN** all chunks for a book are produced and handed to the embedding step
- **THEN** the record `status` is set to `Completed`, `chunk_count` is set to the number of chunks produced, and `ingested_at` is set to the current UTC timestamp
