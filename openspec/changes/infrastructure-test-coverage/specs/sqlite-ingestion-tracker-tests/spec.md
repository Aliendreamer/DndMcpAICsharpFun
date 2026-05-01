## ADDED Requirements

### Requirement: SqliteIngestionTracker persists and retrieves records
The system SHALL store and retrieve `IngestionRecord` entities via EF Core backed by SQLite.

#### Scenario: CreateAsync assigns an Id and returns the record
- **WHEN** `CreateAsync` is called with a valid `IngestionRecord`
- **THEN** the returned record has a non-zero `Id` and matches the input fields

#### Scenario: GetByIdAsync returns the correct record
- **WHEN** a record exists and `GetByIdAsync` is called with its Id
- **THEN** the correct record is returned

#### Scenario: GetByIdAsync returns null for missing Id
- **WHEN** `GetByIdAsync` is called with an Id that does not exist
- **THEN** `null` is returned

#### Scenario: GetAllAsync returns all records
- **WHEN** multiple records have been created and `GetAllAsync` is called
- **THEN** all created records are returned

### Requirement: SqliteIngestionTracker updates record status
The system SHALL update record fields for each pipeline stage.

#### Scenario: MarkHashAsync updates FileHash
- **WHEN** `MarkHashAsync` is called with a recordId and a hash string
- **THEN** the record's `FileHash` matches the provided hash

#### Scenario: MarkExtractedAsync sets status to Extracted
- **WHEN** `MarkExtractedAsync` is called for a record
- **THEN** the record's `Status` is `IngestionStatus.Extracted`

#### Scenario: MarkJsonIngestedAsync sets status and chunk count
- **WHEN** `MarkJsonIngestedAsync` is called with a chunk count
- **THEN** the record's `Status` is `IngestionStatus.JsonIngested` and `ChunkCount` matches

#### Scenario: MarkFailedAsync sets status and failure reason
- **WHEN** `MarkFailedAsync` is called with an error message
- **THEN** the record's `Status` is `IngestionStatus.Failed` and `FailureReason` matches

#### Scenario: ResetForReingestionAsync resets status to Pending
- **WHEN** `ResetForReingestionAsync` is called on a non-Pending record
- **THEN** the record's `Status` is `IngestionStatus.Pending` and `FailureReason` is cleared

### Requirement: SqliteIngestionTracker deletes records
The system SHALL remove records from the database.

#### Scenario: DeleteAsync removes the record
- **WHEN** `DeleteAsync` is called with an existing Id
- **THEN** a subsequent `GetByIdAsync` for that Id returns `null`
