## ADDED Requirements

### Requirement: BooksAdminEndpoints register book via file upload
The system SHALL accept PDF uploads and reject invalid inputs.

#### Scenario: Valid PDF upload returns 202
- **WHEN** a valid `.pdf` file is posted to `POST /admin/books/register`
- **THEN** the response is `202 Accepted` with the created record

#### Scenario: Non-PDF extension returns 400
- **WHEN** a file with a non-`.pdf` extension is posted
- **THEN** the response is `400 Bad Request`

#### Scenario: Invalid version string returns 400
- **WHEN** a POST is made with an unrecognised `version` value
- **THEN** the response is `400 Bad Request`

### Requirement: BooksAdminEndpoints register book by path
The system SHALL accept a file path and reject missing files or invalid inputs.

#### Scenario: Valid path returns 202
- **WHEN** a valid existing `.pdf` path is provided
- **THEN** the response is `202 Accepted`

#### Scenario: File not found returns 400
- **WHEN** the provided path does not exist on disk
- **THEN** the response is `400 Bad Request`

#### Scenario: Invalid version returns 400
- **WHEN** the version string is not a valid `DndVersion` enum value
- **THEN** the response is `400 Bad Request`

### Requirement: BooksAdminEndpoints list all books
The system SHALL return all registered books.

#### Scenario: Get all books returns 200
- **WHEN** `GET /admin/books` is called
- **THEN** the response is `200 OK` with the list from the tracker

### Requirement: BooksAdminEndpoints trigger extraction
The system SHALL enqueue extraction jobs and handle conflicts.

#### Scenario: Extract — book not found returns 404
- **WHEN** `POST /admin/books/{id}/extract` is called for an unknown Id
- **THEN** the response is `404 Not Found`

#### Scenario: Extract — book already processing returns 409
- **WHEN** the book's status is `Processing`
- **THEN** the response is `409 Conflict`

#### Scenario: Extract — success returns 202
- **WHEN** the book exists and is not processing
- **THEN** the response is `202 Accepted` and the item is enqueued

### Requirement: BooksAdminEndpoints return extracted file list
The system SHALL return the list of extracted JSON page files.

#### Scenario: Get extracted — not found returns 404
- **WHEN** the book Id does not exist
- **THEN** the response is `404 Not Found`

#### Scenario: Get extracted — success returns 200
- **WHEN** the book exists
- **THEN** the response is `200 OK` with file count and file list

### Requirement: BooksAdminEndpoints trigger JSON ingestion
The system SHALL enqueue JSON ingestion jobs and handle conflicts.

#### Scenario: IngestJson — not found returns 404
- **WHEN** `POST /admin/books/{id}/ingest-json` is called for an unknown Id
- **THEN** the response is `404 Not Found`

#### Scenario: IngestJson — conflict returns 409
- **WHEN** the book's status is `Processing`
- **THEN** the response is `409 Conflict`

#### Scenario: IngestJson — success returns 202
- **WHEN** the book exists and is not processing
- **THEN** the response is `202 Accepted`

### Requirement: BooksAdminEndpoints delete a book
The system SHALL delete books and return appropriate status codes.

#### Scenario: Delete — not found returns 404
- **WHEN** `DELETE /admin/books/{id}` is called for an unknown Id
- **THEN** the response is `404 Not Found`

#### Scenario: Delete — conflict returns 409
- **WHEN** the orchestrator returns `DeleteBookResult.Conflict`
- **THEN** the response is `409 Conflict`

#### Scenario: Delete — success returns 204
- **WHEN** the book is deleted successfully
- **THEN** the response is `204 No Content`

### Requirement: BooksAdminEndpoints cancel extraction
The system SHALL cancel in-progress extraction by book Id.

#### Scenario: Cancel — not found returns 404
- **WHEN** `POST /admin/books/{id}/cancel-extract` is called for an Id with no active extraction
- **THEN** the response is `404 Not Found`

#### Scenario: Cancel — success returns 200
- **WHEN** an active extraction exists for the given Id
- **THEN** the response is `200 OK`
