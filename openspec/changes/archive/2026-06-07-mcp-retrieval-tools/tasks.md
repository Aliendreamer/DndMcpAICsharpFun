## 1. NuGet + Configuration

- [x] 1.1 Add `ModelContextProtocol.AspNetCore` package: `dotnet add package ModelContextProtocol.AspNetCore`
- [x] 1.2 Add `Mcp` section to `Config/appsettings.json`: `{ "Mcp": { "ApiKey": "" } }`
- [x] 1.3 Add dev key to `Config/appsettings.Development.json`: `{ "Mcp": { "ApiKey": "devMcpKey" } }`

## 2. McpOptions + Middleware

- [x] 2.1 Create `Features/Mcp/McpOptions.cs` — record `McpOptions(string ApiKey)` bound to `"Mcp"` config section
- [x] 2.2 Create `Features/Mcp/McpAuthMiddleware.cs` — reads `X-Mcp-Api-Key` header, compares to `McpOptions.ApiKey` via `IOptions<McpOptions>`, returns 401 if missing or wrong, calls `_next` if correct

## 3. Tool Definitions

- [x] 3.1 Create `Features/Mcp/DndMcpTools.cs` — static class with `[McpServerToolType]` attribute; inject `IRagRetrievalService` and `IEntityRetrievalService` via constructor
- [x] 3.2 Implement `search_lore` tool method: parameters `query` (required), `version` (optional), `category` (optional), `topK` (optional, default 5); parse `version` string to `DndVersion?` and `category` to `ContentCategory?`, ignoring unknown values; call `IRagRetrievalService.SearchAsync`; return serialized list of `{ title, text, sourceBook, category, score }`
- [x] 3.3 Implement `search_entities` tool method: parameters `query` (required), `type` (optional), `edition` (optional), `keyword` (optional), `crMax` (optional), `spellLevel` (optional), `srd` (optional), `srd52` (optional), `topK` (optional, default 10); parse `type` to `EntityType?` and `edition` to `DndVersion?`, ignoring unknown values; call `IEntityRetrievalService.SearchAsync`; return serialized list of `{ id, name, type, sourceBook, edition, canonicalText, fields }`
- [x] 3.4 Implement `get_entity` tool method: parameter `id` (required); call `IEntityRetrievalService.GetByIdAsync`; return serialized entity record on success, or `"Entity not found: {id}"` string if null

## 4. Registration

- [x] 4.1 Register options and MCP server in `Program.cs`: `builder.Services.Configure<McpOptions>(...)`, then `builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly()`
- [x] 4.2 Map the MCP endpoint in `Program.cs` after middleware setup: `app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/mcp"), b => b.UseMiddleware<McpAuthMiddleware>()); app.MapMcp("/mcp")`

## 5. HTTP Collection Updates

- [x] 5.1 Add MCP section to `DndMcpAICsharpFun.http` with a comment explaining the `X-Mcp-Api-Key` header and the `/mcp` endpoint (note: MCP uses JSON-RPC, not REST — document the key requirement)
- [x] 5.2 Add corresponding entry to `dnd-mcp-api.insomnia.json`

## 6. Tests

- [x] 6.1 Create `DndMcpAICsharpFun.Tests/Entities/Admin/McpAuthMiddlewareTests.cs` — unit tests: missing key returns 401, wrong key returns 401, correct key calls next middleware
- [x] 6.2 Create `DndMcpAICsharpFun.Tests/Entities/Mcp/DndMcpToolsTests.cs` — unit tests using mocked services: `search_lore` returns results, `search_lore` with unknown version returns empty gracefully, `search_entities` returns results, `search_entities` with unknown type returns empty gracefully, `get_entity` returns entity for known ID, `get_entity` returns not-found message for unknown ID
- [x] 6.3 Run `dotnet build` and `dotnet test` — all tests pass
