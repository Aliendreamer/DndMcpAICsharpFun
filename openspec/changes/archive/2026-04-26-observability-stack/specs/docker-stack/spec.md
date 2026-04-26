## MODIFIED Requirements

### Requirement: Docker Compose defines app, Qdrant, and Ollama services
The system SHALL provide a `docker-compose.yml` that defines six services: `app` (ASP.NET Core), `qdrant` (vector store), `ollama` (embedding model host), `prometheus` (metrics collection), `grafana` (dashboards), and `sqlite-web` (database browser), all on a shared internal network.

#### Scenario: Stack starts cleanly
- **WHEN** `docker compose up` is run from the project root
- **THEN** all six services start and reach a healthy state

#### Scenario: App waits for dependencies
- **WHEN** Qdrant or Ollama has not yet passed its health check
- **THEN** the `app` service does not report healthy until both dependencies are ready

## ADDED Requirements

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
