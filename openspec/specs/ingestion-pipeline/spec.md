# ingestion-pipeline

## Purpose

Defines the requirements for registering, tracking, and ingesting D&D source books (PDFs) into the vector store, including the admin HTTP API, ingestion lifecycle, and background processing.

## Requirements

### Requirement: A PDF book can be registered via the admin API
The system SHALL accept a PDF upload at `POST /admin/books/register` with form fields `sourceName`, `version`, and `displayName`, persist an ingestion record, and return HTTP 201 with the created record.

#### Scenario: Valid PDF is registered successfully
- **WHEN** a PDF file is uploaded with valid `sourceName`, `version`, and `displayName`
- **THEN** the system stores the file, creates an `IngestionRecord` with status `Pending`, and returns HTTP 201

#### Scenario: Duplicate file (same SHA-256 hash) returns existing record
- **WHEN** a PDF with the same content as an already-registered book is uploaded
- **THEN** the system returns HTTP 200 with the existing record without creating a duplicate

#### Scenario: Non-PDF file is rejected
- **WHEN** a file that does not have a `.pdf` extension is uploaded
- **THEN** the system returns HTTP 400

#### Scenario: Invalid version value is rejected
- **WHEN** the `version` field does not match a valid `DndVersion` enum name
- **THEN** the system returns HTTP 400 with a message listing valid values

#### Scenario: Uploaded filename is sanitised against path traversal
- **WHEN** an upload is submitted with a filename containing directory traversal sequences (e.g. `../../etc/passwd`)
- **THEN** the system strips directory components and saves only the base filename

### Requirement: All registered books can be listed
The system SHALL return all ingestion records at `GET /admin/books`.

#### Scenario: Books list is returned
- **WHEN** `GET /admin/books` is called
- **THEN** the system returns HTTP 200 with a JSON array of all `IngestionRecord` objects

### Requirement: A book can be re-ingested on demand
The system SHALL reset a book's ingestion status and trigger a new ingestion run at `POST /admin/books/{id}/reingest`.

#### Scenario: Existing book is queued for re-ingestion
- **WHEN** `POST /admin/books/{id}/reingest` is called with a valid book id
- **THEN** the system resets the record to `Pending` status, starts ingestion asynchronously, and returns HTTP 202

#### Scenario: Unknown book id returns 404
- **WHEN** `POST /admin/books/{id}/reingest` is called with an id that does not exist
- **THEN** the system returns HTTP 404

### Requirement: The ingestion orchestrator processes a book end-to-end
The system SHALL extract text from the PDF, chunk it, embed the chunks, upsert them into the vector store, and mark the record `Completed`.

#### Scenario: Book is ingested successfully
- **WHEN** `IngestBookAsync` is called for a `Pending` record
- **THEN** the system extracts pages, produces chunks, embeds and upserts them, and sets status to `Completed` with a chunk count

#### Scenario: Unchanged book is skipped
- **WHEN** `IngestBookAsync` is called for a `Completed` record whose file hash has not changed
- **THEN** the system skips processing and leaves the record unchanged

#### Scenario: Ingestion failure marks the record as Failed
- **WHEN** an unhandled exception occurs during ingestion
- **THEN** the system sets the record status to `Failed` and stores the error message

### Requirement: A background service automatically processes pending and failed books
The system SHALL poll for `Pending` and `Failed` records on a 24-hour cycle and attempt ingestion for each.

#### Scenario: Pending books are processed on the next cycle
- **WHEN** the background service cycle runs and there are `Pending` records
- **THEN** the service attempts ingestion for each record in sequence
