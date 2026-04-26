# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A D&D-themed ASP.NET Core Web API on .NET 10.0 intended to expose MCP (Model Context Protocol) tools for AI integration. The project is in early development — `Infrastructure/` is a placeholder for the infrastructure layer and `Program.cs` contains only the minimal host setup.

## Commands

```bash
# Build
dotnet build

# Run (default port: http://localhost:5101)
dotnet run

# Run with hot reload
dotnet watch run

# Restore packages
dotnet restore
```

There are no tests yet. When added, run them with `dotnet test`.

## Architecture

- **Program.cs** — entry point; ASP.NET Core minimal hosting setup
- **Config/** — `appsettings.json` and `appsettings.Development.json` (loaded automatically by the host)
- **Infrastructure/** — intended for infrastructure-layer code (data access, external clients, etc.); currently empty

### Key project settings

- Target framework: `net10.0`
- Nullable reference types: enabled
- Implicit usings: enabled
- `Microsoft.AspNetCore.OpenApi` is included for OpenAPI/Swagger support

### Configuration

The host listens on `http://localhost:5101` (set in `DndMcpAICsharpFun.http`). Override via `launchSettings.json` or environment variables if needed.

## Observability

When the full stack is running (`docker compose up`), these UIs are available:

| Service | URL | Notes |
|---------|-----|-------|
| Grafana | http://localhost:3000 | Pre-provisioned dashboards for .NET, Qdrant, Ollama |
| Prometheus | http://localhost:9090 | Metrics scraping and querying |
| sqlite-web | http://localhost:8080 | Browse `IngestionRecords` table |
| Qdrant UI | http://localhost:6333/dashboard | Vector collection browser |

> **Note:** The `/metrics` endpoint is unauthenticated and intended for local development only. Do not expose it in production. It can be disabled by setting `OpenTelemetry:Enabled: false` in configuration.

## API Contracts

`DndMcpAICsharpFun.http` at the project root is the authoritative runnable reference for all API endpoints.

**Rule:** When adding, changing, or removing any HTTP endpoint (`MapGet`, `MapPost`, `MapPut`, `MapDelete`), update `DndMcpAICsharpFun.http` in the same commit. Every registered route must have a corresponding example request in the file.
