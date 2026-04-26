## ADDED Requirements

### Requirement: Operator can register a PDF book via admin endpoint
The system SHALL provide a `POST /admin/books/register` endpoint (protected by API key) that accepts book metadata and creates an ingestion record with status `Pending`.

#### Scenario: Valid registration creates a pending record
- **WHEN** `POST /admin/books/register` is called with valid `filePath`, `sourceName`, `version`, and `displayName`
- **THEN** the system returns HTTP 201 with the created record id and status `Pending`

#### Scenario: Registering a file path that does not exist returns 422
- **WHEN** `POST /admin/books/register` is called with a `filePath` that does not exist on the books volume
- **THEN** the system returns HTTP 422 with an error describing the missing file

#### Scenario: Duplicate registration of identical file returns existing record
- **WHEN** `POST /admin/books/register` is called for a file whose SHA256 hash already exists with status `Completed`
- **THEN** the system returns HTTP 200 with the existing record and does not create a duplicate

### Requirement: Operator can list all registered books
The system SHALL provide a `GET /admin/books` endpoint that returns all ingestion records with their current status and metadata.

#### Scenario: Returns all records
- **WHEN** `GET /admin/books` is called with a valid API key
- **THEN** the system returns HTTP 200 with an array of all registered books including id, displayName, sourceName, version, status, chunkCount, and ingestedAt

### Requirement: Operator can force reprocessing of a specific book
The system SHALL provide a `POST /admin/books/{id}/reingest` endpoint that resets a record's status to `Pending` and triggers immediate ingestion.

#### Scenario: Reingest resets and reprocesses
- **WHEN** `POST /admin/books/{id}/reingest` is called for an existing record
- **THEN** the record status is reset to `Pending`, ingestion runs immediately, and the endpoint returns HTTP 202 Accepted
