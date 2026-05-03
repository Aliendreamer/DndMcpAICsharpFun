## ADDED Requirements

### Requirement: A single page can be extracted synchronously for inspection
The system SHALL provide `POST /admin/books/{id}/extract-page/{pageNumber}` that extracts the specified page inline (not queued), runs the full LLM extraction pass, and returns the enriched page JSON object in the response body.

#### Scenario: Valid page number returns enriched JSON
- **WHEN** `POST /admin/books/{id}/extract-page/51` is called for an existing book
- **THEN** the system returns HTTP 200 with the enriched page object `{ page, raw_text, blocks, entities }`

#### Scenario: Unknown book id returns 404
- **WHEN** the book id does not exist
- **THEN** the system returns HTTP 404

#### Scenario: Page number out of PDF range returns 400
- **WHEN** the requested page number exceeds the total page count of the PDF
- **THEN** the system returns HTTP 400

### Requirement: Single-page extraction does not persist by default
The system SHALL return the result without writing to disk unless the query parameter `save=true` is present.

#### Scenario: Default call does not modify stored files
- **WHEN** `POST /admin/books/{id}/extract-page/{pageNumber}` is called without `?save=true`
- **THEN** no file is written to `extracted/<bookId>/page_<n>.json`

#### Scenario: save=true persists the result
- **WHEN** `POST /admin/books/{id}/extract-page/{pageNumber}?save=true` is called
- **THEN** the result is written to `extracted/<bookId>/page_<n>.json` and returned in the response
