## ADDED Requirements

### Requirement: Grafana is provisioned with Prometheus datasource at startup
The system SHALL run a Grafana container that auto-provisions a Prometheus datasource pointing to `http://prometheus:9090` via a bind-mounted provisioning YAML, requiring no manual UI configuration.

#### Scenario: Datasource is available immediately after startup
- **WHEN** `docker compose up` completes and Grafana is healthy
- **THEN** the Grafana datasource list contains a Prometheus entry with URL `http://prometheus:9090` in state `OK`

#### Scenario: Anonymous access is enabled for local development
- **WHEN** a browser navigates to `http://localhost:3000`
- **THEN** Grafana loads without prompting for credentials

### Requirement: Grafana includes pre-provisioned dashboards for all three services
The system SHALL auto-provision at least three dashboards via bind-mounted JSON: one for the .NET runtime and HTTP metrics, one for Qdrant, and one for Ollama. Each dashboard SHALL load without errors and display panels backed by the Prometheus datasource.

#### Scenario: .NET dashboard loads with data
- **WHEN** the .NET dashboard is opened in Grafana after the app has received traffic
- **THEN** panels for request rate, request duration p99, GC collections, and thread pool queue length display data

#### Scenario: Qdrant dashboard loads with data
- **WHEN** the Qdrant dashboard is opened after at least one vector search
- **THEN** panels for collection count, search request rate, and gRPC request duration display data

#### Scenario: Dashboard survives container restart
- **WHEN** the Grafana container is stopped and restarted
- **THEN** all three dashboards are still present and display data without any manual re-import

### Requirement: Prometheus data is persisted across restarts
The system SHALL mount a named Docker volume for Prometheus TSDB storage so that scraped metrics are not lost when the container restarts.

#### Scenario: Historical data survives Prometheus restart
- **WHEN** the Prometheus container is restarted after collecting data for 5 minutes
- **THEN** Grafana panels show continuous data across the restart boundary
