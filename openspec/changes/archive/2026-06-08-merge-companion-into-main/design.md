## Context

Today the system is two ASP.NET Core projects in one repo:

- `DndMcpAICsharpFun` (`app`, port 5101) — ingestion/RAG API + MCP **server** (`ModelContextProtocol.AspNetCore`), EF Core + `IngestionDbContext` on `data/ingestion.db`. `Program.cs` wires options, infrastructure clients, ingestion, retrieval, observability, health, and the MCP server.
- `DndMcpAICompanion` (`companion`, port 5102) — Blazor Server UI (`Components/`, `wwwroot/`) with `Auth`, `Campaign`, `Chat` features. Persists users/campaigns/heroes/snapshots/chat to `data/companion.db` via **raw ADO.NET** repositories (hand-written `CREATE TABLE`, `AUTOINCREMENT`, `last_insert_rowid()`). Its chat is an MCP **client** (`McpClient` over `HttpClientTransport`) pointed at `http://app:5101/mcp`.

The main csproj already excludes the companion (`DefaultItemExcludes` includes `DndMcpAICompanion/**`), so the two compile independently. Both target `net10.0` via centralized `Directory.Build.props`; package versions are centrally managed (CPM). A `"Mcp"` config section name is used by **both** projects for different shapes (server `McpOptions` vs client Url+ApiKey).

This change is the first of a sequence: **(1) merge → (2) SQLite→Postgres**. It is purely structural — no storage-engine change, no UI behavior change.

## Goals / Non-Goals

**Goals:**
- One deployable host (port 5101) serving API + MCP server + Blazor UI.
- One `Domain/` folder owning every domain model type.
- One EF Core `AppDbContext` covering all tables; raw ADO.NET removed; `IngestionDbContext` absorbed.
- Cookie auth for the UI coexisting with the existing API-key and MCP-key auth.
- `DndMcpAICompanion` and `DndMcpAICompanion.Tests` projects deleted; their code and tests re-homed.
- Behavior parity: same pages, routes, chat behavior, and ingestion/retrieval endpoints as before.

**Non-Goals:**
- Migrating storage to Postgres (Change 2).
- Replacing the loopback MCP client with in-process tool invocation (later optimization).
- Any change to UI pages, routes, or chat UX.
- Reworking the existing ingestion/retrieval/MCP-server code beyond what the merge requires.

## Decisions

### D1: Single project, files moved by feature — not a shared library
The companion's `Components/`, `wwwroot/`, and `Features/{Auth,Campaign,Chat}` move into the main project under the same folder names; `DefaultItemExcludes` drops `DndMcpAICompanion/**`. Alternative (shared class library referenced by two hosts) was rejected: the user's directive is "companion is just the UI," i.e. one host, not a library boundary.

### D2: One unified `AppDbContext` (SQLite now)
A new `Infrastructure/Persistence/AppDbContext` defines `DbSet`s for `IngestionRecord`, `User`, `Campaign`, `Hero`, `HeroSnapshot`, and chat persistence (if any is persisted today). The existing `IngestionDbContext` is replaced by it. EF migrations are regenerated as a single fresh `InitialCreate` for the unified context (the existing ingestion migrations are dropped, since there is no production data to preserve and Change 2 will re-baseline on Postgres anyway). `HeroSnapshot.CharacterSheet` continues to be stored as JSON (EF `string` column with converter), matching today's `CharacterJson`.

Alternative (two coexisting DbContexts on two SQLite files) was rejected: it would leave the "1 db" goal half-done and make Change 2 a double migration.

### D3: Repositories rewritten to EF Core, keeping their interfaces
`UserRepository`, `CampaignRepository`, `HeroRepository` keep their public method shapes (so the Blazor pages and services are untouched) but their bodies become EF Core (`DbContext` LINQ / `SaveChangesAsync`, identity columns via the provider, `RETURNING`/generated keys handled by EF). The hand-written `CREATE TABLE` init (`InitializeDatabaseAsync`) is replaced by EF migrations applied at startup (`MigrateDatabaseAsync`, already used by the main host).

### D4: Config section rename `Mcp` (client) → `McpClient`
The MCP **server** keeps `"Mcp"` (`McpOptions`, already bound in the main program). The companion's client config (`Url`, `ApiKey`) binds to a new `"McpClient"` section. The loopback default becomes `http://localhost:5101/mcp`. Compose env vars change `Mcp__Url`/`Mcp__ApiKey` → `McpClient__Url`/`McpClient__ApiKey` for the merged `app` service.

### D5: Startup wiring merged via extensions
The companion's `Extensions/` registrations (`AddDndChat`, `AddMcpClient`, `AddDndAuthentication`, `AddDndRateLimiting`, `AddDndBlazor`, `UseDndMiddleware`, `MapDndEndpoints`) are merged into the main `Extensions/ServiceCollectionExtensions` + `WebApplicationExtensions`, preserving the main program's existing options/infra/MCP-server wiring. The MCP client is created at startup against the loopback endpoint after the host is listening; to avoid the current "create client before build" ordering issue in a single process, the MCP client/tools are resolved lazily (on first chat use) rather than blocking startup on a self-call.

### D6: Auth coexistence
Cookie authentication (companion UI) and the existing API-key middleware (`/admin`) + MCP-key middleware (`/mcp`) coexist: cookie auth guards Blazor pages/circuits; the key middlewares remain path-scoped via `UseWhen`/`MapGroup` as today. No single global auth scheme is imposed.

## Risks / Trade-offs

- **MCP client self-call at startup** → The companion creates the MCP client and lists tools *before* `app.Build()`. In one process that would deadlock (server not listening yet). **Mitigation:** lazy MCP client initialization (D5) — defer `CreateAsync`/`ListToolsAsync` to first use, or register a hosted-service warmup that runs after the server is up.
- **Dropping EF migrations history** → Regenerating a single `InitialCreate` discards the ingestion migration chain. **Mitigation:** acceptable — no production data to preserve; Change 2 re-baselines on Postgres. Document the reset.
- **Blazor + API on one host** → static assets, routing, and antiforgery interactions. **Mitigation:** keep Blazor endpoints and API route groups distinct; main program already has antiforgery hooks commented — enable as needed for the UI only.
- **Behavior regressions in re-homed pages** → **Mitigation:** the merged `DndMcpAICompanion.Tests` suite must pass against the new host; manual smoke of login → campaign → hero → chat.
- **Config drift** (two appsettings merged) → **Mitigation:** single `Config/appsettings.json`; remove the companion's file; verify env-var overrides in both composes.

## Migration Plan

1. Move files into the main project; remove the exclude; add package references (`ModelContextProtocol` client, `Microsoft.Extensions.AI`).
2. Consolidate domain types into `Domain/`; fix namespaces.
3. Add `AppDbContext` + unified migration; rewrite repositories to EF Core; delete `IngestionDbContext` and ADO.NET init.
4. Merge startup extensions + config; rename `Mcp`→`McpClient`; lazy MCP client.
5. Delete `DndMcpAICompanion` + `DndMcpAICompanion.Tests` projects; fold tests into main test project.
6. Update `docker-compose.yml`, `docker-compose.prod.yml` (drop `companion` service + `companion_data`), root `Dockerfile`, docs (`CLAUDE.md`, `.http`, insomnia).
7. Verify: `dotnet build` (warnings-as-errors), full test suite, manual UI smoke.

**Rollback:** revert the merge commit/branch; the two-project layout is restored intact.

## Open Questions

- *(Resolved)* Chat history is **not** persisted today (stateless per request). This change introduces chat persistence: `AppDbContext` gains a `ChatMessage` table (keyed to user/hero/campaign as appropriate), and the chat flow saves user/assistant turns and can replay prior history. This is the one net-new behavior in an otherwise structural change; it is intentionally scoped small (store + load turns, no analytics/summarization).
