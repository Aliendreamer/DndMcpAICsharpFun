## Why

The stack runs three stateful services (ASP.NET Core app, Qdrant, Ollama) with no visibility into health, latency, or resource usage; diagnosing performance problems or capacity issues requires guesswork. Adding a Prometheus + Grafana observability layer and lightweight admin UIs for SQLite and Qdrant gives developers actionable runtime data without leaving the local environment.

## What Changes

- Add OpenTelemetry instrumentation to the .NET app (metrics exported in Prometheus format via `/metrics` endpoint)
- Add Prometheus service to Docker Compose, configured to scrape the app, Qdrant, and Ollama
- Add Grafana service to Docker Compose with auto-provisioned datasource and starter dashboards for all three services
- Add SQLite Web UI service (sqlite-web) to Docker Compose, mounted to the same `app_data` volume
- Expose the Qdrant built-in Web UI (already served at `:6333/dashboard`) via a clearly documented port mapping
- Extend `appsettings.json` with an `OpenTelemetry` configuration section

## Capabilities

### New Capabilities

- `metrics-collection`: OTel instrumentation in the .NET service, Prometheus scrape targets for app + Qdrant + Ollama
- `observability-dashboards`: Grafana container with provisioned Prometheus datasource and pre-built dashboards
- `data-browsers`: SQLite Web UI and Qdrant Web UI access in Docker Compose

### Modified Capabilities

- `docker-stack`: New services (prometheus, grafana, sqlite-web) added to compose; new named volumes; port mappings documented

## Impact

- **NuGet**: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Exporter.Prometheus.AspNetCore`
- **docker-compose.yml**: 4 new services, 3 new named volumes, new bind-mount config files
- **New config files**: `infra/prometheus/prometheus.yml`, `infra/grafana/provisioning/datasources/prometheus.yml`, `infra/grafana/provisioning/dashboards/dashboard.yml`, dashboard JSON files
- **Program.cs**: OTel registration wired into the host builder
- **appsettings.json**: `OpenTelemetry` section (metrics endpoint path, enabled flag)
- **No breaking changes to existing API endpoints or data models**
