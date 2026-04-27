## MODIFIED Requirements

### Requirement: A PDF book can be registered via the admin API
The system SHALL accept a PDF upload at `POST /admin/books/register` with form fields `sourceName`, `version`, and `displayName`, persist an ingestion record with status `Pending`, and return HTTP 202 with the created record. Registration SHALL NOT start any ingestion pipeline automatically.

#### Scenario: Valid PDF is registered successfully
- **WHEN** a PDF file is uploaded with valid `sourceName`, `version`, and `displayName`
- **THEN** the system stores the file, creates an `IngestionRecord` with status `Pending`, and returns HTTP 202

#### Scenario: Non-PDF file is rejected
- **WHEN** a file that does not have a `.pdf` extension is uploaded
- **THEN** the system returns HTTP 400

#### Scenario: Invalid version value is rejected
- **WHEN** the `version` field does not match a valid `DndVersion` enum name
- **THEN** the system returns HTTP 400 with a message listing valid values

#### Scenario: Uploaded filename is sanitised against path traversal
- **WHEN** an upload is submitted with a filename containing directory traversal sequences (e.g. `../../etc/passwd`)
- **THEN** the system strips directory components and saves only the base filename

#### Scenario: Registration does not trigger ingestion
- **WHEN** a valid PDF is uploaded and registered
- **THEN** the book remains in `Pending` status until an operator explicitly calls `/reingest`, `/extract`, or `/ingest-json`

## REMOVED Requirements

### Requirement: A background service automatically processes pending and failed books
**Reason**: The LLM extraction pipeline (`/extract` + `/ingest-json`) is the intended ingestion path. Silent background processing conflicts with explicit operator control and runs the legacy chunking pipeline unnecessarily on every registration.
**Migration**: Call `POST /admin/books/{id}/reingest` to run the legacy chunking path for any `Pending` book, or `POST /admin/books/{id}/extract` followed by `POST /admin/books/{id}/ingest-json` for LLM extraction.
