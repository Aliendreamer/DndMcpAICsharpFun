## ADDED Requirements

### Requirement: Persistence tests run against real Postgres via Testcontainers
The system's persistence tests SHALL execute against a real PostgreSQL instance provided by Testcontainers, so they exercise the Npgsql provider and Postgres SQL behavior rather than a substitute engine. A single container SHALL be shared across the persistence-test collection.

#### Scenario: One container serves the persistence collection
- **WHEN** the persistence-test collection runs
- **THEN** a single `postgres:18-alpine` container is started once, the `AppDbContext` is pointed at it, and migrations/schema are applied

#### Scenario: Non-persistence tests need no database
- **WHEN** tests outside the persistence collection run
- **THEN** they do not start a Postgres container

### Requirement: Tests are isolated with Respawn
The system SHALL reset database state between persistence tests using Respawn, so tests are independent without recreating the container per test.

#### Scenario: State does not leak between tests
- **WHEN** one persistence test inserts rows and a subsequent test runs
- **THEN** the subsequent test sees a clean database (tables reset by Respawn)
