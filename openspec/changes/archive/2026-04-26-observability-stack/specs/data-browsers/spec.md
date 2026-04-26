## ADDED Requirements

### Requirement: SQLite Web UI is accessible via browser
The system SHALL run a `coleifer/sqlite-web` container mounted to the same `app_data` volume at `/data/ingestion.db`, accessible at `http://localhost:8080`, allowing developers to browse and query ingestion records.

#### Scenario: Ingestion records table is visible
- **WHEN** a browser navigates to `http://localhost:8080`
- **THEN** the `IngestionRecords` table is listed and rows are browsable

#### Scenario: SQL queries can be executed
- **WHEN** a developer enters a SQL SELECT statement in the sqlite-web query console
- **THEN** the results are returned and displayed in the browser

### Requirement: Qdrant Web UI is documented and accessible
The system SHALL document that the Qdrant built-in Web UI is served at `http://localhost:6333/dashboard` via the existing port mapping, allowing developers to browse collections and run test queries.

#### Scenario: Qdrant dashboard loads
- **WHEN** a browser navigates to `http://localhost:6333/dashboard`
- **THEN** the Qdrant Web UI loads and lists existing collections

#### Scenario: Collection content can be inspected
- **WHEN** a developer selects a collection in the Qdrant Web UI
- **THEN** vector count, payload schema, and sample points are displayed
