# ingestion-pipeline (delta)

## MODIFIED Requirements

### Requirement: A PDF book can be registered via the admin API
The system SHALL accept a PDF upload at `POST /admin/books/register` with form fields `sourceName`, `version`, `displayName`, and `tocPage` (int, required), persist an ingestion record, and return HTTP 202 with the created record.

#### Scenario: Valid PDF with tocPage is registered successfully
- **WHEN** a PDF file is uploaded with valid `sourceName`, `version`, `displayName`, and `tocPage`
- **THEN** the system stores the file, creates an `IngestionRecord` with status `Pending` and the provided `tocPage`, and returns HTTP 202

#### Scenario: Non-PDF file is rejected
- **WHEN** a file that does not have a `.pdf` extension is uploaded
- **THEN** the system returns HTTP 400

#### Scenario: Invalid version value is rejected
- **WHEN** the `version` field does not match a valid `DndVersion` enum name
- **THEN** the system returns HTTP 400 with a message listing valid values

#### Scenario: Missing tocPage is rejected
- **WHEN** a PDF is uploaded without a `tocPage` field
- **THEN** the system returns HTTP 400

#### Scenario: Uploaded filename is sanitised against path traversal
- **WHEN** an upload is submitted with a filename containing directory traversal sequences
- **THEN** the system strips directory components and saves only the base filename

## REMOVED Requirements

### Requirement: A PDF book can be registered by server-side path
**Reason**: Replaced by file upload only. Server-side path registration (`POST /admin/books/register-path`) is removed to simplify the API surface.
**Migration**: Use `POST /admin/books/register` with a file upload instead.
