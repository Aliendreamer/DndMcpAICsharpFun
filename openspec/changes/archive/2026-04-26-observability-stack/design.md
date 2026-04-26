## Context

The current stack (ASP.NET Core app + Qdrant + Ollama) runs entirely in Docker Compose with no instrumentation. Developers have no visibility into request latency, embedding throughput, Qdrant query times, Ollama model load, or SQLite ingestion record counts. The only diagnostic path is docker logs. Adding a standard Prometheus + Grafana observability stack closes this gap for both local development and lightweight production monitoring.

## Goals / Non-Goals

**Goals:**
- Instrument the .NET app with OpenTelemetry (ASP.NET Core, HttpClient, runtime metrics) exported as Prometheus scrape endpoint
- Add Prometheus service that scrapes the app, Qdrant native metrics endpoint, and Ollama metrics endpoint
- Add Grafana with fully-provisioned datasource and starter dashboards (no manual click-through setup)
- Add sqlite-web container bound to the app's SQLite database for ad-hoc table inspection
- Document how to reach the Qdrant built-in Web UI (already on port 6333/dashboard)
- Keep all new services opt-in for CI (profiles or separate compose override)

**Non-Goals:**
- Distributed tracing (spans, Jaeger, Tempo) — metrics only in this iteration
- Alerting rules or PagerDuty integration
- Log aggregation (Loki, ELK)
- Production-grade security for admin UIs (sqlite-web, Grafana anonymous access are dev-only)
- Custom Grafana dashboard authoring — only starter dashboards provisioned

## Decisions

### D1: OTel Prometheus pull export via `/metrics` HTTP endpoint

**Choice**: `OpenTelemetry.Exporter.Prometheus.AspNetCore` which adds a `/metrics` route directly to the existing Kestrel host.

**Alternatives considered**:
- *OTLP push to an OTel Collector* — more flexible but adds another container and moving parts for what is essentially a local dev stack.
- *App.Metrics / Prometheus.NET standalone* — older ecosystem, OTel is the forward-looking standard in .NET.

**Rationale**: Single dependency, no extra container, Prometheus can scrape `app:5101/metrics` directly on the internal network.

### D2: Grafana provisioning via bind-mounted YAML + JSON (dashboards-as-code)

**Choice**: Mount `infra/grafana/provisioning/` into the container so datasource and dashboards are configured at startup — no manual Grafana UI interaction required.

**Rationale**: Reproducible, version-controlled, survives `docker compose down -v`. Grafana anonymous access enabled in dev (GF_AUTH_ANONYMOUS_ENABLED=true) to avoid a login prompt during development.

### D3: sqlite-web for SQLite inspection

**Choice**: `coleifer/sqlite-web` image, bind-mounted to the same `app_data` volume at `/data/ingestion.db`.

**Alternatives considered**:
- *Adminer* — heavier, multi-database oriented, overkill for a single SQLite file.
- *DB Browser for SQLite desktop app* — not accessible via browser, can't run in compose.

**Rationale**: Lightweight (~50 MB image), single-file focused, read-write capable, available on `:8080`.

### D4: Qdrant Web UI via existing port 6333

**Choice**: No new container needed — Qdrant serves a built-in React UI at `http://localhost:6333/dashboard`. The existing port mapping already exposes this.

**Rationale**: Zero effort, already bundled. Just needs documentation.

### D5: Prometheus scrape targets for Qdrant and Ollama

- **Qdrant** exposes Prometheus metrics at `:6333/metrics` natively. No extra config needed on the Qdrant side.
- **Ollama** does not expose a `/metrics` endpoint by default. The `ollama/ollama` image does expose basic process metrics if `OLLAMA_PROMETHEUS=true` env var is set (available since Ollama 0.2+). We set this in compose.

### D6: New infra directory for config files

All bind-mounted config lives under `infra/` in the repo root:
```
infra/
  prometheus/
    prometheus.yml
  grafana/
    provisioning/
      datasources/prometheus.yml
      dashboards/dashboard.yml
    dashboards/
      dotnet.json
      qdrant.json
      ollama.json
```

This keeps config close to the compose file without polluting the .NET project directory.

## Risks / Trade-offs

- **Metrics endpoint unauthenticated** → The `/metrics` endpoint is exposed on the same port as the API. On the internal Docker network this is low-risk; publicly reachable deployments should add middleware to gate it behind `X-Admin-Api-Key` or move it to a separate port. Document this caveat.

- **Ollama metrics availability** → `OLLAMA_PROMETHEUS=true` is undocumented in older Ollama releases. If metrics don't appear, the Prometheus scrape will simply return empty. Dashboard panels will show "No data" rather than crashing.

- **sqlite-web write access** → sqlite-web can mutate the ingestion database. This is intentional for developer use (e.g., re-queuing a failed record) but dangerous if exposed publicly. Document: dev-only, not to be included in production compose.

- **Grafana volume vs. bind mount** → Dashboards are provisioned via bind-mount so they're always in sync with the repo. If a developer customises a dashboard in the Grafana UI, their changes will be lost on restart. Expected trade-off: this is a starter/read-only dashboard, not a persistent workspace.

## Migration Plan

1. Add `infra/` directory and config files (no runtime changes)
2. Add NuGet packages and OTel registration to `Program.cs`
3. Update `docker-compose.yml` with new services and volumes
4. Run `docker compose up` — existing services unaffected; new services join the same network
5. Rollback: remove new services from compose, remove OTel registration — fully reversible

## Open Questions

- Should `/metrics` be gated behind the admin API key middleware? Deferred: document the risk, leave open for a follow-up security hardening change.
- Grafana dashboard IDs to import from grafana.com vs. hand-crafted? Decision: use grafana.com community dashboards for Qdrant (ID: 20046) and .NET runtime (ID: 17706); hand-craft Ollama panel (limited community dashboards exist).
