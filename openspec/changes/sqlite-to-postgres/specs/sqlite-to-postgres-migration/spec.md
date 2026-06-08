## ADDED Requirements

### Requirement: Existing SQLite rows are migrated into Postgres once
The system SHALL provide a one-off migrator that copies all existing rows (ingestion records and any companion rows) from the legacy SQLite database into Postgres, so prior registrations are preserved without re-ingestion. The migrator SHALL be runnable at cutover and SHALL NOT be part of normal application startup.

#### Scenario: Registrations carry over
- **WHEN** the migrator runs against a SQLite database containing the DMG and Tasha's ingestion records
- **THEN** those records exist in Postgres with the same fields (display name, hash, status, chunk count, source key) and no re-ingest is required

#### Scenario: Identity keys and sequences are correct
- **WHEN** rows with explicit primary keys are copied into Postgres identity columns
- **THEN** the keys are preserved and each table's identity sequence is advanced past the maximum copied id, so new inserts do not collide

#### Scenario: Migration is verifiable
- **WHEN** the migrator completes
- **THEN** per-table row counts in Postgres match the source SQLite counts

### Requirement: SQLite dependency is retired after cutover
The system SHALL retain SQLite packages only for the migrator. Once migration is complete, the running application SHALL have no SQLite dependency.

#### Scenario: App has no SQLite reference post-cutover
- **WHEN** the application project is inspected after the migrator is retired
- **THEN** it references no SQLite EF provider or `Microsoft.Data.Sqlite` for runtime persistence
