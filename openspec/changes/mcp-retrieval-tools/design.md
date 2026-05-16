## Context

The project is an ASP.NET Core Web API on .NET 10 using minimal APIs and vertical slices. It already exposes `IRagRetrievalService` (prose block search) and `IEntityRetrievalService` (structured entity search) via REST endpoints. No MCP scaffolding exists. The eventual consumer is a separate Blazor companion app that will run an AI agent loop and call this server as its knowledge tool backend.

## Goals / Non-Goals

**Goals:**

- Expose a Streamable HTTP MCP server at `/mcp`
- Protect the endpoint with a dedicated `X-Mcp-Api-Key` (separate from admin key)
- Provide three tools: `search_lore`, `search_entities`, `get_entity`
- Design `Features/Mcp/` so future tool files are discovered automatically with no registration changes

**Non-Goals:**

- Conversation history or session state (lives in the Blazor companion)
- Admin/ingestion tools over MCP (admin key remains separate)
- stdio transport (HTTP only; the companion calls over the network)
- Any MCP Resources or Prompts (tools only for now)

## Decisions

**D1 — Official .NET MCP SDK (`ModelContextProtocol.AspNetCore`)**
The SDK handles JSON-RPC framing, `initialize`/`tools/list`/`tools/call` protocol lifecycle, and Streamable HTTP transport. Hand-rolling the protocol would duplicate correct, maintained behaviour. `WithToolsFromAssembly()` means new tool files auto-register.

**D2 — `McpAuthMiddleware` rather than endpoint filter**
Middleware runs before the MCP SDK handler, so an unauthenticated request never enters the MCP stack. An endpoint filter would require the SDK to expose a filter hook, which it doesn't in the current version. Middleware is the standard ASP.NET Core pattern for this.

**D3 — Separate `Mcp:ApiKey` config key**
The admin key grants write access (ingest, normalize, fix-types). MCP clients should only read. A separate key lets the companion be configured with read-only credentials. No code coupling between the two keys.

**D4 — Tools inject services directly, not via HTTP**
The MCP tools are in the same process as the retrieval services. Injecting `IRagRetrievalService` / `IEntityRetrievalService` directly avoids an unnecessary HTTP round-trip and keeps latency low.

**D5 — Tool parameter design: flat strings over enums**
MCP tool parameters are serialized as JSON Schema. Using `string` with description hints (e.g., `"Edition2014 | Edition2024"`) gives Claude flexibility and avoids schema enum validation errors if the AI passes a slightly different value. The tool method validates the string internally and maps to the C# enum.

## Risks / Trade-offs

- **MCP SDK API stability** → The `ModelContextProtocol` package is pre-1.0. Pin to a specific version in the `.csproj` and review on upgrades.
- **No request-level auth token forwarding** → The MCP key authenticates the client connection, not individual tool calls. This is standard for MCP but means all tool calls from a connected client share the same permission level.
- **Tool descriptions drive AI behaviour** → If descriptions are vague, Claude will call the wrong tool or pass wrong parameters. The `[Description]` attributes on methods and parameters are critical documentation.

## Migration Plan

1. Add NuGet package
2. Add config keys to `appsettings.json` and `appsettings.Development.json`
3. Implement `Features/Mcp/` slice (options, middleware, tools)
4. Register in `Program.cs`
5. Update `.http` and `.insomnia.json`
6. Tests pass (`dotnet test`)
7. No existing endpoints are touched — zero rollback risk

## Open Questions

None — all decisions resolved in design.
