## Why

The user-facing companion currently lives in a separate project (`DndMcpAICompanion`) with its own host, its own SQLite database, and hand-written ADO.NET repositories — yet it is "just the UI." That split forces a second container, a second database file, an HTTP MCP hop between two of our own processes, and domain models scattered across two projects. Collapsing the companion into the main project gives us one deployable, one `Domain/` folder, and one persistence layer — the prerequisite structural step before consolidating storage onto Postgres.

## What Changes

- **BREAKING**: The `DndMcpAICompanion` project is deleted. Its Blazor UI (`Components/`, `wwwroot/`), `Auth`, `Campaign`, and `Chat` features move into the main `DndMcpAICsharpFun` project, which now serves the API, the MCP server, and the Blazor UI from one host on port 5101.
- All domain models (`User`, `Campaign`, `Hero`, `HeroSnapshot`, `CharacterSheet`, chat message types, and the relocated `IngestionRecord`) are consolidated into the single `Domain/` folder. Repositories and services stop defining their own model types.
- The companion's raw ADO.NET repositories (`UserRepository`, `CampaignRepository`, `HeroRepository` — `AUTOINCREMENT`, `last_insert_rowid()`, hand-written `CREATE TABLE`) are rewritten as EF Core against a single unified `AppDbContext` that also absorbs the existing `IngestionDbContext`. **Persistence stays on SQLite in this change** — the engine swap to Postgres is a separate follow-up.
- The chat MCP client transport changes from the cross-container endpoint (`http://app:5101/mcp`) to a loopback endpoint within the single process. (In-process tool invocation is a deferred optimization.)
- The `"Mcp"` configuration section collision is reconciled: the MCP **server** keeps `"Mcp"` (`McpOptions`); the MCP **client** config (Url + ApiKey) moves to a distinct `"McpClient"` section.
- **BREAKING**: The `companion` Docker service (port 5102) and `companion_data` volume are removed from both `docker-compose.yml` and `docker-compose.prod.yml`; the `app` service now serves the UI.
- Companion configuration (Ollama model, rate limits, MCP client) merges into the main `Config/appsettings.json`. The `DndMcpAICompanion.Tests` project is folded into the main test suite.
- **NEW behavior**: chat history begins to be persisted. Today chat is stateless per request; this change adds a `ChatMessage` table to `AppDbContext` and saves/loads conversation turns. Scope is intentionally minimal (store and replay turns — no summarization or analytics).

## Capabilities

### New Capabilities
- `unified-app-host`: a single ASP.NET Core host serving the ingestion/RAG/MCP API, the MCP server, and the Blazor companion UI as one deployable, with cookie auth for the UI coexisting with API-key/MCP-key auth for the API.
- `unified-persistence`: a single EF Core `AppDbContext` (SQLite, this change) covering ingestion records plus user/campaign/hero/snapshot/chat tables, replacing the raw ADO.NET repositories and the standalone `IngestionDbContext`.
- `domain-model-consolidation`: all domain model types live in the one `Domain/` folder, owned independently of persistence and feature code.

### Modified Capabilities
- `companion-program-structure`: **removed** — the companion no longer has a standalone host/`Program.cs`; its startup wiring is absorbed by the main program (see `unified-app-host`).
- `mcp-client-integration`: the MCP client now reads from a `McpClient` config section and defaults to a loopback endpoint inside the merged process rather than a separate `app` container.

> `program-structure` (main program still delegates to extensions — requirement remains true, only additive wiring) and `docker-stack` (no companion-specific requirement exists; the `companion` service removal is an implementation detail) are touched at the implementation level only and do not need delta specs in this change.

## Impact

- **Deleted**: `DndMcpAICompanion/` project (csproj, `Program.cs`, `Dockerfile`, `Config/`, its `data/companion.db`), `DndMcpAICompanion.Tests` project (merged into main tests).
- **Moved into main project**: `Components/`, `wwwroot/`, `Features/Auth`, `Features/Campaign`, `Features/Chat`, plus the companion's `Extensions/` registrations.
- **Modified**: `DndMcpAICsharpFun.csproj` (remove `DndMcpAICompanion/**` from `DefaultItemExcludes`; add client/AI/Blazor package references), `Program.cs` + `Extensions/`, `Config/appsettings.json`, `docker-compose.yml`, `docker-compose.prod.yml`, root `Dockerfile`, `CLAUDE.md`, `DndMcpAICsharpFun.http`, `dnd-mcp-api.insomnia.json`.
- **Persistence**: one `AppDbContext` + regenerated EF migrations (fresh initial create for the unified context); raw ADO.NET removed. SQLite retained.
- **Out of scope**: Postgres migration, in-process MCP tool invocation, any change to UI behavior or page routes.
