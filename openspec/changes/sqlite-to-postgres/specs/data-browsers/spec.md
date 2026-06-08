## MODIFIED Requirements

### Requirement: SQLite Web UI is accessible via browser
The system SHALL run a `dpage/pgadmin4` (pgAdmin) container, accessible at `http://localhost:8080`, connected to the Postgres database, allowing developers to browse and query the application's tables. (Replaces the former `coleifer/sqlite-web` container, which cannot read PostgreSQL.)

#### Scenario: Tables are visible

- **WHEN** a browser navigates to `http://localhost:8080` and connects to the Postgres server
- **THEN** the application's tables (including `IngestionRecords`) are listed and rows are browsable

#### Scenario: SQL queries can be executed

- **WHEN** a developer enters a SQL SELECT statement in the pgAdmin query tool
- **THEN** the results are returned and displayed in the browser
