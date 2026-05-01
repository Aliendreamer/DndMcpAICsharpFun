## ADDED Requirements

### Requirement: An admin endpoint cancels a running extraction
The system SHALL expose `POST /admin/books/{id}/cancel-extract` that stops the currently running extraction for the given book ID.

#### Scenario: Cancel stops a running extraction
- **WHEN** `POST /admin/books/{id}/cancel-extract` is called while extraction is in progress for that book
- **THEN** the system cancels the extraction, returns HTTP 200

#### Scenario: Cancel on a book with no running extraction returns 404
- **WHEN** `POST /admin/books/{id}/cancel-extract` is called for a book that is not currently being extracted
- **THEN** the system returns HTTP 404

#### Scenario: Cancel endpoint requires admin key
- **WHEN** `POST /admin/books/{id}/cancel-extract` is called without a valid `X-Admin-Api-Key` header
- **THEN** the system returns HTTP 401

### Requirement: Cancelled extraction cleans up partial output and resets status
The system SHALL delete all files under `extracted/{bookId}/` and set the book's `IngestionRecord` status back to `Pending` when an extraction is cancelled.

#### Scenario: Partial JSON files are deleted on cancel
- **WHEN** an extraction is cancelled after some pages have been written
- **THEN** the `extracted/{bookId}/` directory and all its contents are deleted

#### Scenario: Book status resets to Pending on cancel
- **WHEN** an extraction is cancelled
- **THEN** the `IngestionRecord.Status` is set to `Pending` and the book can be re-extracted
