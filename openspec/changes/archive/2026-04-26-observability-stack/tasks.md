## 1. NuGet Packages and OTel Registration

- [x] 1.1 Add `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Instrumentation.Http`, and `OpenTelemetry.Exporter.Prometheus.AspNetCore` to `DndMcpAICsharpFun.csproj`
- [x] 1.2 Add `OpenTelemetry` section to `appsettings.json` with `"Enabled": true` and `"ServiceName": "dnd-mcp-api"`
- [x] 1.3 Add `OpenTelemetry` section to `appsettings.Development.json` with `"Enabled": true`
- [x] 1.4 Create `OpenTelemetryOptions` class in `Infrastructure/` with `Enabled` and `ServiceName` properties
- [x] 1.5 Register OTel in `Program.cs`: bind `OpenTelemetryOptions`, conditionally add `AddOpenTelemetry()` with ASP.NET Core, runtime, and HttpClient instrumentation, and `AddPrometheusExporter()`
- [x] 1.6 Map the `/metrics` endpoint in `Program.cs` via `app.MapPrometheusScrapingEndpoint()`
- [x] 1.7 Run `dotnet build` — 0 errors

## 2. Prometheus Configuration

- [x] 2.1 Create directory `infra/prometheus/`
- [x] 2.2 Create `infra/prometheus/prometheus.yml` with three scrape jobs: `dnd-app` (app:5101/metrics, 15s), `qdrant` (qdrant:6333/metrics, 15s), `ollama` (ollama:11434/metrics, 30s)

## 3. Grafana Configuration

- [x] 3.1 Create directories `infra/grafana/provisioning/datasources/`, `infra/grafana/provisioning/dashboards/`, and `infra/grafana/dashboards/`
- [x] 3.2 Create `infra/grafana/provisioning/datasources/prometheus.yml` provisioning the Prometheus datasource at `http://prometheus:9090`
- [x] 3.3 Create `infra/grafana/provisioning/dashboards/dashboard.yml` pointing to `/var/lib/grafana/dashboards`
- [x] 3.4 Create `infra/grafana/dashboards/dotnet.json` — .NET dashboard with panels: HTTP request rate, request duration p99, GC collections total, thread pool queue length, heap size (use Grafana dashboard ID 17706 as base, adapted for the OTel metric names)
- [x] 3.5 Create `infra/grafana/dashboards/qdrant.json` — Qdrant dashboard with panels: REST request rate, gRPC request rate, collection count, indexed vectors count (community dashboard ID 20046 adapted)
- [x] 3.6 Create `infra/grafana/dashboards/ollama.json` — Ollama dashboard with panels: request duration, active requests, model load time (hand-crafted from `ollama_*` metric names)

## 4. Docker Compose Expansion

- [x] 4.1 Add `prometheus` service to `docker-compose.yml`: image `prom/prometheus:v3.4.0`, port `9090:9090`, bind-mount `./infra/prometheus/prometheus.yml:/etc/prometheus/prometheus.yml:ro`, named volume `prometheus_data:/prometheus`, healthcheck on `localhost:9090/-/healthy`, `dnd_net` network
- [x] 4.2 Add `grafana` service to `docker-compose.yml`: image `grafana/grafana:12.0.1`, port `3000:3000`, env vars `GF_AUTH_ANONYMOUS_ENABLED=true` and `GF_AUTH_ANONYMOUS_ORG_ROLE=Admin`, bind-mount provisioning and dashboards dirs, named volume `grafana_data:/var/lib/grafana`, `depends_on: prometheus`, `dnd_net` network
- [x] 4.3 Add `sqlite-web` service to `docker-compose.yml`: image `coleifer/sqlite-web:latest`, port `8080:8080`, command `-H 0.0.0.0 /data/ingestion.db`, volume `app_data:/data`, `depends_on: app`, `dnd_net` network
- [x] 4.4 Add `OLLAMA_PROMETHEUS=true` environment variable to the `ollama` service in `docker-compose.yml`
- [x] 4.5 Add `prometheus_data` and `grafana_data` to the `volumes:` top-level section
- [x] 4.6 Verify `docker compose config` parses without errors

## 5. Documentation

- [x] 5.1 Add a `## Observability` section to `CLAUDE.md` (or create a `docs/observability.md`) documenting the URLs: Grafana `:3000`, Prometheus `:9090`, sqlite-web `:8080`, Qdrant UI `:6333/dashboard`
- [x] 5.2 Note the `/metrics` endpoint caveat (unauthenticated, dev-only) in the same doc
