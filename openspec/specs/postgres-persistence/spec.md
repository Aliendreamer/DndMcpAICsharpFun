# postgres-persistence Specification

## Purpose
TBD - created by archiving change sqlite-to-postgres. Update Purpose after archive.
## Requirements
### Requirement: Relational data is persisted in PostgreSQL via Npgsql
The system SHALL persist all relational data (ingestion records, users, campaigns, heroes, snapshots, chat turns) in PostgreSQL through the single `AppDbContext` using the Npgsql provider. The SQLite provider SHALL NOT be used by the running application.

#### Scenario: App connects to Postgres on startup
- **WHEN** the application starts with a valid Postgres connection configured
- **THEN** `AppDbContext` connects via Npgsql and EF migrations are applied, creating all tables

#### Scenario: No SQLite at runtime
- **WHEN** the application's persistence configuration is inspected
- **THEN** the context is registered with `UseNpgsql` and no `UseSqlite`/SQLite file path is used

### Requirement: Database connection is configured by a connection string
The system SHALL read the Postgres connection (host, port, database, user, password) from configuration, overridable by environment variables, rather than a SQLite file path. Secrets SHALL NOT be committed; production credentials SHALL come from environment/secret configuration.

#### Scenario: Connection comes from configuration
- **WHEN** the app builds the data-context options
- **THEN** the connection string is composed from configuration/environment, not a hardcoded value

#### Scenario: Startup tolerates Postgres readiness
- **WHEN** Postgres is briefly unavailable at startup
- **THEN** the connection retries (retry-on-failure) rather than crashing immediately

### Requirement: Model mapping is preserved across the provider change
The system SHALL preserve the existing `AppDbContext` model: the same tables, indexes, unique constraints, string-converted enums, and the `CharacterSheet` JSON column — with no schema or behavior change visible to repositories or the API.

#### Scenario: Repository behavior is unchanged
- **WHEN** any repository method runs against Postgres
- **THEN** it returns the same shapes and results as before, including the campaign hero-count projection and JSON character-sheet round-trip

