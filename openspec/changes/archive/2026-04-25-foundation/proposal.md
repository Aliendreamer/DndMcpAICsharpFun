## Why

The project is a blank ASP.NET Core host with no structure. Before any feature work (ingestion, embedding, retrieval, MCP) can begin, the project needs a deployable foundation: a folder structure, a Docker Compose stack (app + Qdrant + Ollama), typed configuration, and wired infrastructure clients — so all subsequent changes build on a consistent, runnable base.

## What Changes

- Establish feature-sliced folder structure under `Domain/`, `Features/`, `Infrastructure/`
- Add Docker Compose with three services: `app`, `qdrant`, `ollama`
- Add typed configuration models for Qdrant, Ollama, and ingestion options
- Register `Qdrant.Client`, `OllamaSharp`, and `PdfPig` as infrastructure clients in DI
- Add health check endpoints for Qdrant and Ollama connectivity
- Add admin endpoint security via API key middleware
- Wire `appsettings.json` with all required configuration sections

## Capabilities

### New Capabilities

- `infrastructure-clients`: Qdrant, Ollama, and PdfPig clients registered and configured via DI
- `docker-stack`: Compose file defining app, Qdrant, and Ollama services with volumes and networking
- `admin-security`: API key middleware protecting all `/admin/*` routes

### Modified Capabilities

## Impact

- `Program.cs` — gains service registrations, middleware, health checks
- `DndMcpAICsharpFun.csproj` — gains NuGet packages: `Qdrant.Client`, `OllamaSharp`, `PdfPig`, `Microsoft.Data.Sqlite`
- `Config/appsettings.json` — gains `Qdrant`, `Ollama`, `Ingestion`, `Admin` config sections
- New: `docker-compose.yml`, `Dockerfile`
- New: `Infrastructure/` subdirectories for each client
- No breaking changes — project currently has no functionality
