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
