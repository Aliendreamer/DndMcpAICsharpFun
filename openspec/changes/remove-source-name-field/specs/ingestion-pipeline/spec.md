# ingestion-pipeline (delta)

## MODIFIED Requirements

### Requirement: A PDF book can be registered via the admin API
The system SHALL accept a PDF upload at `POST /admin/books/register` with form fields `version`, `displayName`, and an optional `bookType` (one of `Core`, `Supplement`, `Adventure`, `Setting`, `Unknown` — case-insensitive; missing or unparseable values default to `Unknown` without HTTP 400). The handler SHALL persist an ingestion record carrying these fields, stream the multipart body directly to disk (no double-buffering), and store the file under a server-generated GUID name; the user-supplied filename is retained only as a sanitised display value. The handler SHALL NOT require or use a `sourceName` form field.

#### Scenario: Valid PDF is registered successfully
- **WHEN** a PDF file is uploaded with valid `version` and `displayName`
- **THEN** the system stores the file under `{BooksPath}/{guid}.pdf`, creates an `IngestionRecord` with status `Pending`, and returns HTTP 202

#### Scenario: Non-PDF file is rejected
- **WHEN** a file that does not have a `.pdf` extension is uploaded
- **THEN** the system returns HTTP 400 and the partially uploaded file (if any) is deleted

#### Scenario: Invalid version value is rejected
- **WHEN** the `version` field does not match a valid `DndVersion` enum name
- **THEN** the system returns HTTP 400 with a message listing valid values

#### Scenario: Uploaded filename is sanitised against path traversal
- **WHEN** an upload is submitted with a filename containing directory traversal sequences
- **THEN** the system stores the file under a server-generated GUID name and the user-supplied name is retained only as a sanitised display value

#### Scenario: sourceName field is silently ignored
- **WHEN** a caller includes a legacy `sourceName` form part
- **THEN** registration succeeds normally and the value is discarded; no `SourceName` is persisted on the record

## REMOVED Requirements

### Requirement: IngestionRecord persists a separate sourceName tag
**Reason**: The field was a holdover from the LLM-extraction era when prompts referenced a short source tag. After the LLM ingestion path was removed, no part of the active pipeline reads `IngestionRecord.SourceName` — it is not propagated to Qdrant, not consulted by retrieval, not surfaced anywhere except the unfiltered `GET /admin/books` listing. `displayName` covers every real need.
**Migration**: The EF migration drops the `SourceName` column from `IngestionRecords`. Operators who relied on inspecting `sourceName` in the admin list use `displayName` instead.
