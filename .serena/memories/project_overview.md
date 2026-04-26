# Project Overview

**DndMcpAICsharpFun** — A D&D-themed ASP.NET Core Web API on .NET 10.0 that exposes MCP (Model Context Protocol) tools for AI integration.

## Tech Stack
- .NET 10.0
- ASP.NET Core minimal hosting
- Nullable reference types: enabled
- Implicit usings: enabled
- Microsoft.AspNetCore.OpenApi (for OpenAPI/Swagger support)

## Key Architecture Components
- **Program.cs** — entry point, minimal ASP.NET Core hosting setup
- **Config/** — appsettings.json and appsettings.Development.json
- **Infrastructure/** — infrastructure-layer code (data access, external clients, etc.)
- **Features/** — feature modules (Ingestion, Embedding, Retrieval, Admin, etc.)

## Important Rules
1. Every class with a `private static partial class Log` and `[LoggerMessage]` methods MUST be declared `public sealed partial class` (not just `public sealed class`)
2. The source generator emits a sibling partial declaration for the outer type
3. Never create launchSettings.json; configure via appsettings.json + ASPNETCORE_ENVIRONMENT Docker build arg

## Default Port
http://localhost:5101 (set in DndMcpAICsharpFun.http; override via launchSettings.json or environment variables)
