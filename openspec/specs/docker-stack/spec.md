# docker-stack

## Purpose

Defines the containerisation and Docker Compose requirements for running the full application stack locally and in production.
## Requirements
### Requirement: Docker Compose defines app, Qdrant, and Ollama services
The system SHALL provide a `docker-compose.yml` that defines six services: `app` (ASP.NET Core), `qdrant` (vector store), `ollama` (embedding model host), `prometheus` (metrics collection), `grafana` (dashboards), and `sqlite-web` (database browser), all on a shared internal network.

#### Scenario: Stack starts cleanly
- **WHEN** `docker compose up` is run from the project root
- **THEN** all six services start and reach a healthy state

#### Scenario: App waits for dependencies
- **WHEN** Qdrant or Ollama has not yet passed its health check
- **THEN** the `app` service does not report healthy until both dependencies are ready

### Requirement: Persistent volumes for books and Qdrant data
The system SHALL define named Docker volumes: one for the PDF books directory (mounted into `app`) and one for Qdrant storage data.

#### Scenario: Books volume is mounted in app container
- **WHEN** the `app` container starts
- **THEN** the books volume is accessible at the configured `Ingestion:BooksPath`

#### Scenario: Qdrant data survives container restart
- **WHEN** the `qdrant` container is restarted
- **THEN** previously stored collections and vectors are still present

### Requirement: Dockerfile uses multi-stage build
The system SHALL provide a `Dockerfile` with a build stage (`sdk:10.0`) and a runtime stage (`aspnet:10.0`), producing a minimal final image.

#### Scenario: Image builds successfully
- **WHEN** `docker build` is run from the project root
- **THEN** the image is produced without error and contains no SDK tooling

### Requirement: Admin API key is injected via environment variable
The system SHALL read the admin API key from the encrypted `Config/appsettings.Production.json` file loaded automatically by the ASP.NET Core host at runtime. The `Admin__ApiKey` environment variable SHALL NOT be defined in `docker-compose.yml`.

#### Scenario: Key is loaded from encrypted config
- **WHEN** the `app` container starts with git-crypt-decrypted config files present
- **THEN** the app reads `Admin:ApiKey` from `Config/appsettings.Production.json` without any environment variable override

### Requirement: Prometheus and Grafana services are defined in Docker Compose
The system SHALL define `prometheus` (prom/prometheus:latest) and `grafana` (grafana/grafana:latest) services in `docker-compose.yml` on the `dnd_net` network, with named volumes for persistent storage and bind-mounted config from the `infra/` directory.

#### Scenario: Prometheus is reachable on host port 9090
- **WHEN** `docker compose up` is run
- **THEN** `http://localhost:9090` serves the Prometheus UI

#### Scenario: Grafana is reachable on host port 3000
- **WHEN** `docker compose up` is run
- **THEN** `http://localhost:3000` serves the Grafana UI

### Requirement: sqlite-web service is defined in Docker Compose
The system SHALL define a `sqlite-web` service using `coleifer/sqlite-web` mounted to the `app_data` volume at `/data/ingestion.db`, exposed on host port `8080`.

#### Scenario: sqlite-web is reachable on host port 8080
- **WHEN** `docker compose up` is run and the app has created the SQLite database
- **THEN** `http://localhost:8080` serves the sqlite-web UI with `IngestionRecords` visible

### Requirement: Named volumes include prometheus and grafana storage
The system SHALL define named volumes `prometheus_data` and `grafana_data` in `docker-compose.yml` in addition to the existing `books_data`, `qdrant_data`, `app_data`, and `ollama_data` volumes.

#### Scenario: Prometheus volume persists data
- **WHEN** the `prometheus` container is restarted
- **THEN** previously scraped metrics remain queryable

#### Scenario: Grafana volume persists settings
- **WHEN** the `grafana` container is restarted
- **THEN** provisioned datasources and dashboards are still present

### Requirement: ASPNETCORE_ENVIRONMENT is sourced dynamically from the shell
The system SHALL configure the `app` service in `docker-compose.yml` to read `ASPNETCORE_ENVIRONMENT` from the host shell environment via `${ASPNETCORE_ENVIRONMENT}`, allowing the value to be controlled by `start.sh` without hardcoding.

#### Scenario: Development environment is set via start.sh
- **WHEN** `./start.sh Development` is run
- **THEN** the `app` container receives `ASPNETCORE_ENVIRONMENT=Development` and loads `Config/appsettings.Development.json`

#### Scenario: Production environment is set via start.sh
- **WHEN** `./start.sh Production` is run
- **THEN** the `app` container receives `ASPNETCORE_ENVIRONMENT=Production` and loads `Config/appsettings.Production.json`

