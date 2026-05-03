# ingestion-pipeline (delta)

## MODIFIED Requirements

### Requirement: A PDF book can be registered via the admin API
The system SHALL accept a PDF upload at `POST /admin/books/register` with form fields `sourceName`, `version`, `displayName`, and an optional `bookType`. The handler SHALL stream the multipart body directly to disk, store the file under a server-generated GUID name, and persist an `IngestionRecord` whose `BookType` is the parsed enum value (case-insensitive) or `BookType.Unknown` when the field is missing or unparseable. The handler SHALL return HTTP 202 with the created record on success.

#### Scenario: Valid PDF with bookType is registered successfully
- **WHEN** a PDF file is uploaded with valid `sourceName`, `version`, `displayName`, and `bookType=Supplement`
- **THEN** the system stores the file, creates an `IngestionRecord` with `BookType == Supplement` and status `Pending`, and returns HTTP 202

#### Scenario: Valid PDF without bookType registers as Unknown
- **WHEN** a PDF is uploaded with no `bookType` form field
- **THEN** the resulting `IngestionRecord` has `BookType == BookType.Unknown` and registration succeeds

#### Scenario: Invalid bookType value silently defaults to Unknown
- **WHEN** a PDF is uploaded with `bookType=NotARealValue`
- **THEN** the resulting `IngestionRecord` has `BookType == BookType.Unknown` and registration succeeds (no HTTP 400)

#### Scenario: bookType is case-insensitive
- **WHEN** a PDF is uploaded with `bookType=supplement` (lowercase)
- **THEN** the resulting `IngestionRecord` has `BookType == Supplement`

#### Scenario: Non-PDF file is rejected
- **WHEN** a file that does not have a `.pdf` extension is uploaded
- **THEN** the system returns HTTP 400 and the partially uploaded file (if any) is deleted

#### Scenario: Invalid version value is rejected
- **WHEN** the `version` field does not match a valid `DndVersion` enum name
- **THEN** the system returns HTTP 400 with a message listing valid values

### Requirement: Block ingestion propagates BookType from record to point
The block-ingestion orchestrator SHALL copy the record's `BookType` into every `BlockMetadata` produced for that book, and the vector-store service SHALL write `book_type` onto every Qdrant point's payload.

#### Scenario: Newly ingested book has BookType on every point
- **WHEN** `IngestBlocksAsync` runs for a record with `BookType == Core`
- **THEN** every Qdrant point upserted for it has `payload.book_type == "Core"`
