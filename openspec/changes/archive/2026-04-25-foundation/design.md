## Context

The project is a net10.0 ASP.NET Core minimal API with a single 7-line `Program.cs` and no packages beyond `Microsoft.AspNetCore.OpenApi`. The goal is to establish the structural and infrastructure foundation that all subsequent changes (ingestion, embedding, retrieval, MCP) will build on, without implementing any of those features.

The target runtime environment is Docker Compose: one network, three services (`app`, `qdrant`, `ollama`), persistent volumes for books and Qdrant data.

## Goals / Non-Goals

**Goals:**
- Folder structure that supports feature-sliced vertical organisation
- Docker Compose stack that is runnable locally and portable to any Docker host
- Typed options classes bound from `appsettings.json` for all infrastructure clients
- DI registrations for `Qdrant.Client`, `OllamaSharp`, and `PdfPig` — ready to inject, not yet used
- Health check endpoints: `GET /health` (app) and `GET /health/ready` (checks Qdrant + Ollama reachability)
- API key middleware applied to all `/admin/*` routes

**Non-Goals:**
- Any ingestion, embedding, or retrieval logic
- MCP endpoints
- Authentication beyond the single admin API key
- Production hardening (TLS, secrets management, observability)

## Decisions

### D1 — Feature-sliced folder layout over layer-first

```
DndMcpAICsharpFun/
  Domain/
  Features/
    Ingestion/
    Embedding/
    VectorStore/
    Retrieval/
    Admin/
  Infrastructure/
    Ollama/
    Qdrant/
    Sqlite/
```

Rationale: Each feature will have its own ingestion → embedding → storage path. Grouping by feature keeps cohesion high and avoids cross-layer imports. Layer-first (Controllers/, Services/, Repositories/) would create unnecessary coupling for a pipeline-oriented workload.

### D2 — Direct clients over Microsoft Semantic Kernel

Use `Qdrant.Client` (official), `OllamaSharp`, and `PdfPig` directly rather than abstracting through Semantic Kernel.

Rationale: The user explicitly chose Option B (lean, direct). SK adds abstraction over Qdrant and Ollama that reduces visibility into the stack — the opposite of the learning goal. Direct clients are easier to version-pin, debug, and replace independently.

### D3 — API key middleware for admin routes

A single `X-Admin-Api-Key` request header checked against a configured value. Applied only to `/admin/*` via route-based middleware.

Alternatives considered:
- ASP.NET Core built-in API key auth: more ceremony for a single key scenario
- JWT: overkill for an internal admin surface

Rationale: Simple, explicit, no extra packages. The key is loaded from configuration (environment variable in Docker Compose), not hardcoded.

### D4 — Health checks via `Microsoft.Extensions.Diagnostics.HealthChecks`

Custom `IHealthCheck` implementations for Qdrant (ping collections endpoint) and Ollama (ping `/api/tags`). Exposed on `/health/ready`.

Rationale: Standard ASP.NET Core pattern, integrates with Docker Compose `healthcheck:` directive. Allows Compose to gate app startup on dependency readiness.

### D5 — Dockerfile: multi-stage, SDK + runtime

```
Stage 1 (build):  mcr.microsoft.com/dotnet/sdk:10.0
Stage 2 (runtime): mcr.microsoft.com/dotnet/aspnet:10.0
```

Rationale: Keeps the final image small. Standard pattern for .NET containerisation.

## Risks / Trade-offs

- **OllamaSharp API surface changes** → pin to a specific NuGet version; review on upgrade
- **Qdrant.Client version mismatch with Qdrant server image** → pin both in `csproj` and `docker-compose.yml` to matching versions; document in README
- **PdfPig multi-column extraction quality unknown** → foundation only registers the client; actual quality is assessed during the ingestion-pipeline change
- **Single admin API key** → sufficient for local/private deployment; not suitable if the admin surface is ever exposed publicly

## Migration Plan

1. Add NuGet packages
2. Create folder structure (empty placeholder files where needed)
3. Add typed options and DI registrations to `Program.cs`
4. Add health checks
5. Add admin API key middleware
6. Add `Dockerfile` and `docker-compose.yml`
7. Update `appsettings.json` / `appsettings.Development.json`
8. `dotnet build` must pass; `docker compose up` must reach healthy state

Rollback: the change is purely additive — nothing is removed from the existing host setup.
