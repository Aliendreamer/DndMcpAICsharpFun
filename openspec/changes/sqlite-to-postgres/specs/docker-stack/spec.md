## MODIFIED Requirements

### Requirement: sqlite-web service is defined in Docker Compose
The system SHALL define a `postgres` service (`postgres:18-alpine`, with `POSTGRES_DB`/`POSTGRES_USER`/`POSTGRES_PASSWORD`, a named `postgres_data` volume, and a `pg_isready` healthcheck) and a `pgadmin` service (`dpage/pgadmin4`) exposed on host port `8080`. The `app` service SHALL depend on `postgres` being healthy. The former `sqlite-web` service and the `./data` database mount SHALL be removed.

#### Scenario: Postgres is healthy and the app connects

- **WHEN** `docker compose up` is run
- **THEN** the `postgres` service reports healthy (`pg_isready`) and the `app` starts, applies migrations, and serves requests

#### Scenario: pgAdmin is reachable on host port 8080

- **WHEN** the stack is up
- **THEN** `http://localhost:8080` serves the pgAdmin UI, from which the Postgres tables (including `IngestionRecords`) can be browsed

#### Scenario: sqlite-web is gone

- **WHEN** the compose files are inspected
- **THEN** there is no `sqlite-web` service and no `./data` SQLite database mount
