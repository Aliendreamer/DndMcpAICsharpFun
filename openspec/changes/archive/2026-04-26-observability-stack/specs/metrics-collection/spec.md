## ADDED Requirements

### Requirement: .NET app exposes Prometheus metrics endpoint
The system SHALL register OpenTelemetry with ASP.NET Core, runtime, and HttpClient instrumentation, and expose a `/metrics` HTTP endpoint in Prometheus text format via `OpenTelemetry.Exporter.Prometheus.AspNetCore`.

#### Scenario: Metrics endpoint responds to scrape
- **WHEN** an HTTP GET request is made to `GET /metrics`
- **THEN** the response is `200 OK` with `Content-Type: text/plain; version=0.0.4` and contains at least `process_runtime_dotnet` metric lines

#### Scenario: ASP.NET Core request metrics are present
- **WHEN** at least one HTTP request has been served by the app
- **THEN** the `/metrics` output contains `http_server_request_duration_seconds` histogram metrics

#### Scenario: Runtime metrics are present
- **WHEN** the app has been running for at least one GC cycle
- **THEN** the `/metrics` output contains `process_runtime_dotnet_gc_collections_count` or equivalent runtime metric

### Requirement: OTel configuration is driven by appsettings
The system SHALL read an `OpenTelemetry` section from `appsettings.json` with at minimum a `ServiceName` string and an `Enabled` boolean; when `Enabled` is `false` the metrics endpoint SHALL NOT be registered.

#### Scenario: Metrics disabled by configuration
- **WHEN** `OpenTelemetry:Enabled` is `false` in configuration
- **THEN** `GET /metrics` returns `404 Not Found`

#### Scenario: Service name appears in metric labels
- **WHEN** `OpenTelemetry:ServiceName` is set to `"dnd-mcp-api"`
- **THEN** the `/metrics` output contains `service_name="dnd-mcp-api"` in the target info metric

### Requirement: Prometheus scrapes app, Qdrant, and Ollama
The system SHALL provide a `prometheus.yml` configuration that defines three scrape jobs: `dnd-app` targeting `app:5101/metrics`, `qdrant` targeting `qdrant:6333/metrics`, and `ollama` targeting `ollama:11434/metrics`.

#### Scenario: App job is reachable from Prometheus
- **WHEN** Prometheus is running and the app is healthy
- **THEN** the `dnd-app` scrape job shows `UP` state in the Prometheus targets UI

#### Scenario: Qdrant job is reachable from Prometheus
- **WHEN** Prometheus is running and Qdrant is healthy
- **THEN** the `qdrant` scrape job shows `UP` state in the Prometheus targets UI
