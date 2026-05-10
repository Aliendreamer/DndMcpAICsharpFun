## Why

The D&D retrieval API has a rich corpus of structured entities and prose blocks but no way for AI clients to access it directly. Exposing the retrieval layer as an MCP (Model Context Protocol) server lets AI companions — starting with a planned Blazor companion app — query D&D knowledge using the standard tool protocol without bespoke HTTP client code.

## What Changes

- Add `ModelContextProtocol.AspNetCore` NuGet package
- Add `Features/Mcp/` vertical slice with three retrieval tools and auth middleware
- Add `Mcp:ApiKey` configuration section (separate from the admin key)
- Map `/mcp` endpoint in `Program.cs` behind key-checking middleware
- Add example MCP connection entry to `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json`

## Capabilities

### New Capabilities

- `mcp-server`: Streamable HTTP MCP server endpoint at `/mcp`, protected by `X-Mcp-Api-Key`, auto-discovers tools from the assembly
- `mcp-retrieval-tools`: Three MCP tools — `search_lore` (prose RAG), `search_entities` (structured entity search), `get_entity` (fetch by ID) — wiring into existing `IRagRetrievalService` and `IEntityRetrievalService`

### Modified Capabilities

## Impact

- `Program.cs` — two additions: MCP service registration and endpoint mapping
- `Config/appsettings.json` — new `Mcp` section
- `Config/appsettings.Development.json` — dev MCP key
- `DndMcpAICsharpFun.http` — MCP connection example
- `dnd-mcp-api.insomnia.json` — same
- No breaking changes to existing REST endpoints
