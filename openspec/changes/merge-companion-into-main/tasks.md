## 1. Project plumbing & file moves

- [ ] 1.1 Remove `DndMcpAICompanion/**` from `DefaultItemExcludes` in `DndMcpAICsharpFun.csproj`; add package references the companion needs (`ModelContextProtocol` client, `Microsoft.Extensions.AI`) — versions via CPM in `Directory.Packages.props` if not already present
- [ ] 1.2 Move `DndMcpAICompanion/Components/` → `Components/` and `DndMcpAICompanion/wwwroot/` → merge into `wwwroot/`; fix `@namespace`/`_Imports.razor`/`App.razor`/`Routes.razor` references
- [ ] 1.3 Move `DndMcpAICompanion/Features/{Auth,Campaign,Chat}/` → `Features/{Auth,Campaign,Chat}/`; update namespaces from `DndMcpAICompanion.*` to `DndMcpAICsharpFun.*`
- [ ] 1.4 Verify build still excludes nothing stale and the moved Razor components compile (expect failures until later tasks; just confirm files are in scope)

## 2. Domain consolidation

- [ ] 2.1 Move `User`, `Campaign`, `Hero`, `HeroSnapshot`, `CharacterSheet`, and any chat message types into `Domain/`; strip any persistence/UI coupling from them
- [ ] 2.2 Move `IngestionRecord` from `Infrastructure/Sqlite/` into `Domain/`; update references
- [ ] 2.3 Update all usings/namespaces so repositories, services, and components consume domain types from `Domain/`

## 3. Unified persistence (EF Core, still SQLite)

- [ ] 3.1 Add a `ChatMessage` domain type (role, content, timestamp, user/hero/campaign association) under `Domain/`; chat history is now persisted (was stateless)
- [ ] 3.2 Create `Infrastructure/Persistence/AppDbContext` with `DbSet`s for ingestion records + user/campaign/hero/snapshot + `ChatMessage`; configure `HeroSnapshot.CharacterSheet` as a JSON-converted column; configure identity keys
- [ ] 3.3 Delete `IngestionDbContext` and its design-time factory; repoint registrations to `AppDbContext`; keep `IngestionDbContextDesignTimeFactory` equivalent for the new context
- [ ] 3.4 Rewrite `UserRepository`, `CampaignRepository`, `HeroRepository` to EF Core against `AppDbContext`, preserving public method signatures; remove `SqliteConnection`, `CREATE TABLE`, `AUTOINCREMENT`, `last_insert_rowid()`
- [ ] 3.4a Add a `ChatRepository` (EF Core) to save and load `ChatMessage` turns; wire `DndChatService` to persist each user/assistant turn and to load prior history when a conversation is opened
- [ ] 3.5 Remove the companion's imperative DB init (`InitializeDatabaseAsync`); rely on `MigrateDatabaseAsync` at startup
- [ ] 3.6 Delete the old ingestion migrations; generate a fresh single `InitialCreate` migration for `AppDbContext`

## 4. Startup wiring & config merge

- [ ] 4.1 Merge companion `Extensions/` registrations (chat, MCP client, auth, rate limiting, Blazor) into the main `Extensions/ServiceCollectionExtensions` and `WebApplicationExtensions`
- [ ] 4.2 Update `Program.cs` to add Blazor, cookie auth, rate limiting, chat, and MCP-client registration alongside existing API/MCP-server wiring; map Blazor endpoints
- [ ] 4.3 Rename the MCP **client** config from `Mcp` to a new `McpClient` section (bind `McpClientOptions`); leave the MCP **server** `Mcp`/`McpOptions` untouched; default `McpClient:Url` to `http://localhost:5101/mcp`
- [ ] 4.4 Make MCP client initialization lazy (defer `CreateAsync`/`ListToolsAsync` to first chat use, or a post-startup warmup) so the single process does not deadlock self-calling at boot
- [ ] 4.5 Merge `DndMcpAICompanion/Config/appsettings*.json` into `Config/appsettings.json` (Ollama model, RateLimit, McpClient); delete the companion config files
- [ ] 4.6 Ensure cookie auth guards UI pages while `/admin` (API key) and `/mcp` (MCP key) middlewares remain path-scoped and functional

## 5. Delete companion projects & relocate test files

- [ ] 5.1 Delete `DndMcpAICompanion/` (csproj, `Program.cs`, `Dockerfile`, `Config/`, `data/companion.db`, `.dockerignore`, `bin/`, `obj/`)
- [ ] 5.2 `git mv` the 7 companion test files into `DndMcpAICsharpFun.Tests/` (matching subfolders); fix namespaces/usings; delete the `DndMcpAICompanion.Tests` project (bodies rewritten in group 5b)
- [ ] 5.3 Remove any solution/build references to the deleted projects

## 5b. Update & migrate the test suite

- [ ] 5b.1 Update `TrackerFixture` to build `AppDbContext` (was `IngestionDbContext`): swap the `DbContextOptions<>` generic, `UseSqlite`, and ctor; keep open-connection in-memory SQLite + `Migrate()`; share one context-construction helper with `TestDb.NewContext()`
- [ ] 5b.2 Fix the 8 main test files referencing relocated types (`using ...Infrastructure.Sqlite` → `...Domain`; `IngestionDbContext` → `AppDbContext`): `SqliteIngestionTrackerTests`, `BlockIngestionOrchestratorTests`, `BooksAdminEndpointsTests`, `EntityBookDeletionTests`, `IngestEntitiesEndpointTests`, `ExtractEntitiesEndpointTests`, `EntityExtractionOrchestratorTests`, `EntityIngestionOrchestratorTests`
- [ ] 5b.3 Rewrite the 3 relocated companion repo tests (`UserRepositoryTests`, `CampaignRepositoryTests`, `HeroRepositoryTests`) to use `AppDbContext` (in-memory SQLite) instead of connection strings/`SqliteConnection`; drop `InitializeAsync`; keep assertions
- [ ] 5b.4 Port the other 4 relocated tests (`PasswordHasherTests`, `CharacterSheetSerializationTests`, `ChatRateLimiterTests`, `DndChatServiceTests`); update `DndChatServiceTests` for the lazy `McpToolsProvider` + unified `OllamaOptions`; assert a chat turn is persisted via `ChatRepository`
- [ ] 5b.5 Add `Persistence/AppDbContextSmokeTests`: model builds, fresh context migrates + all `DbSet`s queryable, `HeroSnapshot.CharacterSheet` JSON round-trips
- [ ] 5b.6 Verify `coverlet.runsettings`/coverage-exclusion paths still resolve (repoint `Infrastructure/Sqlite` → `Infrastructure/Persistence` if referenced)
- [ ] 5b.7 `dotnet test` — full suite green

## 6. Docker

- [ ] 6.1 In `docker-compose.yml`: delete the `companion` service and the `companion_data` volume; `app` serves the UI on 5101; add `McpClient__Url`/`McpClient__ApiKey` env vars on `app`; keep `sqlite-web`
- [ ] 6.2 In `docker-compose.prod.yml`: apply the same companion-service/volume removal and `McpClient__*` env changes
- [ ] 6.3 Confirm the root `Dockerfile` publishes Blazor static web assets; remove companion Dockerfile assumptions

## 6b. Documentation

- [ ] 6b.1 `CLAUDE.md`: rewrite architecture/overview to one host (API + MCP + UI); update commands, the Observability services table (drop `companion`), add a Persistence note (one `AppDbContext`, chat history persisted), fix `Mcp`→`McpClient` references
- [ ] 6b.2 `README.md`: update description, ports (single 5101, drop 5102), run instructions
- [ ] 6b.3 `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` (kept in sync): add any new user-facing API requests; reflect single-host topology
- [ ] 6b.4 Note that retired/modified companion capabilities are reconciled into `openspec/specs/` at archive time (`openspec archive ... -y`), not hand-edited now

## 7. Verification

- [ ] 7.1 `dotnet build` passes with warnings-as-errors and no excluded/duplicate compile items
- [ ] 7.2 Full merged test suite passes via `dotnet test` (companion tests now run inside the main suite)
- [ ] 7.3 `docker compose up` starts a single `app` service; manual smoke: register/login → create campaign → add hero → chat answers via MCP tools over loopback
- [ ] 7.4 Confirm `data/companion.db` no longer exists and only the unified `AppDbContext` database is created
- [ ] 7.5 Confirm chat turns persist: send messages, reopen conversation, prior turns reload in order
