# Merge Companion into Main Project — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. The canonical task list and acceptance criteria live in `tasks.md`; this file adds file paths, code, and verification detail.

**Goal:** Collapse `DndMcpAICompanion` (Blazor UI) into the main `DndMcpAICsharpFun` host so one project serves the API, MCP server, and UI; consolidate all domain models into `Domain/`; unify persistence on one EF Core `AppDbContext` (SQLite, this change); and begin persisting chat history.

**Architecture:** Single ASP.NET Core (`Microsoft.NET.Sdk.Web`) host on port 5101. Domain types in `Domain/`. One `AppDbContext` replaces `IngestionDbContext` and the raw-ADO.NET companion repos. The chat MCP client runs loopback against the same process (lazy init). Cookie auth (UI) coexists with API-key (`/admin`) and MCP-key (`/mcp`) middleware.

**Tech Stack:** .NET 10, EF Core (SQLite provider, this change), Blazor Server, ModelContextProtocol (server + client), Microsoft.Extensions.AI + Ollama, xUnit/FluentAssertions. CPM (`Directory.Packages.props`); warnings-as-errors.

**Conventions for the implementer:**
- All code reads/edits go through Serena tools (project rule). `Read`/`Edit` on `.cs` files is forbidden.
- Commit after each task group with a clear message; do NOT push.
- Preserve every repository method's public signature — read the existing file before rewriting so callers (Blazor pages) stay untouched.
- Namespaces move from `DndMcpAICompanion.*` to `DndMcpAICsharpFun.*`.

---

## File Structure (decomposition)

| Path | Responsibility |
| --- | --- |
| `Domain/User.cs`, `Domain/Campaign.cs`, `Domain/Hero.cs`, `Domain/HeroSnapshot.cs`, `Domain/CharacterSheet.cs`, `Domain/ChatMessage.cs` | Domain models (moved/added), no persistence/UI coupling |
| `Domain/IngestionRecord.cs`, `Domain/IngestionStatus.cs` | Relocated from `Infrastructure/Sqlite/` |
| `Infrastructure/Persistence/AppDbContext.cs` | The single EF Core context for all tables |
| `Infrastructure/Persistence/AppDbContextDesignTimeFactory.cs` | EF design-time factory |
| `Features/Auth/*`, `Features/Campaign/*`, `Features/Chat/*` | Moved from companion; repos rewritten to EF Core |
| `Components/**`, `wwwroot/**` | Blazor UI moved from companion |
| `Extensions/ServiceCollectionExtensions.cs`, `Extensions/WebApplicationExtensions.cs` | Absorb companion's `Extensions/` registrations |
| `Program.cs` | Composition root for the merged host |
| `Migrations/**` | Regenerated single `InitialCreate` for `AppDbContext` |

---

## Task 1: Bring companion files into the build (no behavior yet)

**Files:**
- Modify: `DndMcpAICsharpFun.csproj` (`DefaultItemExcludes`, package refs)
- Modify: `Directory.Packages.props` (add any missing `PackageVersion`)
- Move: `DndMcpAICompanion/Components/` → `Components/`, `DndMcpAICompanion/wwwroot/` → `wwwroot/`
- Move: `DndMcpAICompanion/Features/{Auth,Campaign,Chat}/` → `Features/{Auth,Campaign,Chat}/`

- [ ] **Step 1:** In `DndMcpAICsharpFun.csproj`, remove `DndMcpAICompanion/**;DndMcpAICompanion.Tests/**` from `DefaultItemExcludes` (keep `Tools/**`, `.worktrees/**`, and the main test project exclusion). Add package references not already present: `Microsoft.Extensions.AI`, `ModelContextProtocol` (client). `Microsoft.Extensions.AI.Ollama`, `ModelContextProtocol.AspNetCore`, `Microsoft.EntityFrameworkCore.Sqlite/Design`, `Microsoft.Data.Sqlite` already exist.
- [ ] **Step 2:** Ensure each new `PackageReference` has a matching `PackageVersion` in `Directory.Packages.props` (CPM — references are version-less). Add `ModelContextProtocol` and `Microsoft.Extensions.AI` versions if missing (match the versions the companion csproj resolved).
- [ ] **Step 3:** Move `Components/` and `wwwroot/` from the companion into the project root (use `git mv` so history is preserved). Merge `wwwroot/app.css`/`app.js` into the existing `wwwroot/` (the main project may not have one yet — then it's a straight move).
- [ ] **Step 4:** Move `Features/Auth`, `Features/Campaign`, `Features/Chat` via `git mv`.
- [ ] **Step 5:** Bulk-update namespaces in the moved files from `DndMcpAICompanion` → `DndMcpAICsharpFun` (and `@namespace`/`@using` in `.razor`, `_Imports.razor`, `App.razor`, `Routes.razor`).
- [ ] **Step 6:** `dotnet build` — expect failures (missing DI wiring, `Mcp` vs `McpClient`, repos still ADO.NET). That's fine; the goal of this task is only that files are in scope and namespaces resolve. Commit: `refactor: move companion UI/features into main project (compiles after wiring)`.

---

## Task 2: Consolidate domain models into `Domain/`

**Files:**
- Create: `Domain/User.cs`, `Domain/Campaign.cs`, `Domain/Hero.cs`, `Domain/HeroSnapshot.cs`, `Domain/CharacterSheet.cs`
- Move: `Infrastructure/Sqlite/IngestionRecord.cs` → `Domain/IngestionRecord.cs`, `Infrastructure/Sqlite/IngestionStatus.cs` → `Domain/IngestionStatus.cs`

- [ ] **Step 1:** Read the existing record/model declarations (Serena `find_symbol`/`get_symbols_overview`) in the moved `Features/Auth/UserRepository.cs`, `Features/Campaign/HeroRepository.cs`, `Features/Campaign/CampaignRepository.cs`, `Features/Campaign/CharacterSheet.cs`. The hero records are: `Hero(long Id, long CampaignId, string Name, DateTime CreatedAt, HeroSnapshot? LatestSnapshot)`, `HeroSnapshot(long Id, long HeroId, int SessionNumber, string SessionLabel, int Level, DateTime CreatedAt, CharacterSheet Sheet)`, `HeroSnapshotMeta(...)`, `HeroWithCampaign(Hero Hero, string CampaignName)`. Get the exact `User`, `Campaign`, and `CharacterSheet` shapes from those files.
- [ ] **Step 2:** Move each domain record/type into its own file under `Domain/` in namespace `DndMcpAICsharpFun.Domain`. Keep them as the same `record`/`class` shapes. Do NOT add EF attributes — mapping goes in `AppDbContext` (Task 3).
- [ ] **Step 3:** `git mv` `IngestionRecord.cs` and `IngestionStatus.cs` into `Domain/`; change namespace to `DndMcpAICsharpFun.Domain`. (`IngestionRecord` currently has `[Required]/[MaxLength]` data annotations — those are persistence-agnostic validation and may stay, or move to fluent config in `AppDbContext`; prefer keeping behavior identical: leave the annotations.)
- [ ] **Step 4:** Update all `using` statements across the solution that referenced the old namespaces (`DndMcpAICsharpFun.Infrastructure.Sqlite` for the record, `DndMcpAICompanion.Features.*` for the models). Use Serena `find_referencing_symbols` to locate them.
- [ ] **Step 5:** `dotnet build` — still expect repo/wiring failures; domain references must resolve. Commit: `refactor: consolidate domain models into Domain/`.

---

## Task 3: Unified `AppDbContext` + EF migrations (SQLite)

**Files:**
- Create: `Domain/ChatMessage.cs`
- Create: `Infrastructure/Persistence/AppDbContext.cs`, `Infrastructure/Persistence/AppDbContextDesignTimeFactory.cs`
- Delete: `Infrastructure/Sqlite/IngestionDbContext.cs`, `Infrastructure/Sqlite/IngestionDbContextDesignTimeFactory.cs`
- Delete + regenerate: `Migrations/**`

- [ ] **Step 1:** Add the `ChatMessage` domain type:

```csharp
namespace DndMcpAICsharpFun.Domain;

public sealed class ChatMessage
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long? HeroId { get; set; }
    public long? CampaignId { get; set; }
    public string Role { get; set; } = string.Empty;   // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2:** Create `AppDbContext` mapping all tables. Carry over the `IngestionRecord` config from the old `IngestionDbContext` verbatim (indexes on `FileHash`/`Status`, `Status` and `BookType` stored as strings). Map `HeroSnapshot.CharacterSheet` as a JSON-converted column. Map identity keys (`long`/`int` auto-increment).

```csharp
using DndMcpAICsharpFun.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DndMcpAICsharpFun.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<IngestionRecord> IngestionRecords => Set<IngestionRecord>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Hero> Heroes => Set<Hero>();
    public DbSet<HeroSnapshot> HeroSnapshots => Set<HeroSnapshot>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<IngestionRecord>(e =>
        {
            e.HasIndex(r => r.FileHash);
            e.HasIndex(r => r.Status);
            e.Property(r => r.Status).HasConversion<string>();
            e.Property(r => r.BookType).HasConversion<string>();
        });

        b.Entity<HeroSnapshot>(e =>
        {
            e.Property(s => s.Sheet).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<CharacterSheet>(v, (JsonSerializerOptions?)null)!);
        });

        b.Entity<ChatMessage>(e =>
        {
            e.HasIndex(m => new { m.UserId, m.CampaignId, m.HeroId });
            e.HasIndex(m => m.CreatedAt);
        });
    }
}
```

> NOTE: `Hero`/`HeroSnapshot` are currently positional `record`s. EF can map them, but if mapping a JSON-converted member on a positional record proves awkward, convert these two to mutable `class`/`record` with settable properties as part of this task — keep field names identical so callers are unaffected.

- [ ] **Step 3:** Create `AppDbContextDesignTimeFactory` (mirror the existing `IngestionDbContextDesignTimeFactory`, swap the type to `AppDbContext`, keep the SQLite connection string it uses).
- [ ] **Step 4:** Delete `IngestionDbContext.cs` + its design-time factory and repoint **every** production injector to `AppDbContext`: `Features/Ingestion/Tracking/SqliteIngestionTracker.cs` (constructor/`DbContextOptions<>` generic), `Extensions/ServiceCollectionExtensions.cs` (DI registration), `Extensions/WebApplicationExtensions.cs` (`MigrateDatabaseAsync`). Use Serena `find_referencing_symbols` on `IngestionDbContext` to confirm none remain.
- [ ] **Step 5:** Delete the existing `Migrations/` folder contents and generate a fresh migration:

```bash
dotnet ef migrations add InitialCreate -c AppDbContext -o Migrations
```
Expected: a single migration creating ingestion + users/campaigns/heroes/snapshots/chat tables.
- [ ] **Step 6:** Commit: `refactor: unify persistence in AppDbContext + add ChatMessage`.

---

## Task 3b: Rewrite repositories to EF Core (TDD)

**Files:**
- Modify: `Features/Auth/UserRepository.cs`, `Features/Campaign/CampaignRepository.cs`, `Features/Campaign/HeroRepository.cs`
- Create: `Features/Chat/ChatRepository.cs`
- Test: `DndMcpAICsharpFun.Tests/Persistence/*RepositoryTests.cs`

Repositories take `AppDbContext` (or `IDbContextFactory<AppDbContext>`) by DI instead of a connection string. Preserve these method signatures (read each file first to confirm exact return types/params):
- `UserRepository`: `FindByUsernameAsync`, `ExistsAsync`, `CreateAsync` (drop `InitializeAsync`).
- `CampaignRepository`: `GetAllAsync`, `GetByIdAsync`, `CreateAsync`, `DeleteAsync` (drop `InitializeAsync`).
- `HeroRepository`: `GetByCampaignAsync`, `GetAllByUserAsync`, `GetByIdAsync`, `GetSnapshotsAsync`, `GetSnapshotAsync`, `CreateAsync`, `SaveSnapshotAsync`, `DeleteAsync` (drop `ReadHero` ADO.NET helper).

- [ ] **Step 1 (test first):** Write `HeroRepositoryTests` using an EF Core SQLite in-memory (or `Microsoft.EntityFrameworkCore.Sqlite` with `DataSource=:memory:` open connection) context. Example:

```csharp
[Fact]
public async Task CreateAsync_then_GetByIdAsync_round_trips_hero()
{
    using var ctx = TestDb.NewContext();
    var repo = new HeroRepository(ctx);
    var id = await repo.CreateAsync(campaignId: 1, name: "Bruenor");
    var hero = await repo.GetByIdAsync(id);
    hero.Should().NotBeNull();
    hero!.Name.Should().Be("Bruenor");
}
```
(Add a `TestDb` helper that builds an `AppDbContext` on an open in-memory SQLite connection and calls `Database.EnsureCreated()`.)
- [ ] **Step 2:** Run: `dotnet test --filter HeroRepositoryTests` — expect FAIL (repo still ADO.NET / ctor mismatch).
- [ ] **Step 3:** Rewrite `HeroRepository` to EF Core: constructor `HeroRepository(AppDbContext db)`; replace each SQL block with LINQ. `GetAllByUserAsync` joins Heroes→Campaigns on `UserId`; latest snapshot via `OrderByDescending(s => s.CreatedAt)`. `CreateAsync` adds + `SaveChangesAsync()` then returns `entity.Id`. `SaveSnapshotAsync` adds a `HeroSnapshot`. Keep return shapes (`Hero`, `HeroWithCampaign`, `HeroSnapshotMeta`).
- [ ] **Step 4:** Run the test — expect PASS.
- [ ] **Step 5:** Repeat Steps 1–4 for `UserRepository` (round-trip create/find/exists, password hash stored) and `CampaignRepository` (create/get-all-by-user/delete cascade behavior).
- [ ] **Step 6:** Add `ChatRepository(AppDbContext db)` with `AddAsync(ChatMessage)` and `GetHistoryAsync(userId, campaignId?, heroId?)` returning messages ordered by `CreatedAt`. Write a round-trip test (save two turns → load in order).
- [ ] **Step 7:** Commit: `refactor: rewrite repositories on EF Core + add ChatRepository (tests green)`.

---

## Task 4: Startup wiring, config merge, lazy MCP client

**Files:**
- Modify: `Extensions/ServiceCollectionExtensions.cs`, `Extensions/WebApplicationExtensions.cs`
- Modify: `Program.cs`
- Modify: `Config/appsettings.json`, `Config/appsettings.Development.json`
- Move/merge: companion `Extensions/*` registrations
- Delete: `DndMcpAICompanion/Config/*`

- [ ] **Step 1:** Fold the companion's registration logic (`AddDatabase`→now `AddSingleton<AppDbContext>`/`AddDbContextFactory`, `AddDndChat`, `AddMcpClient`, `AddDndAuthentication`, `AddDndRateLimiting`, `AddDndBlazor`) into `ServiceCollectionExtensions`. Register repositories (`UserRepository`, `CampaignRepository`, `HeroRepository`, `ChatRepository`) as scoped/singleton consistent with the DbContext lifetime chosen.
- [ ] **Step 2:** Fold the companion's `UseDndMiddleware`, `MapDndEndpoints`, `InitializeDatabaseAsync` (now just `MigrateDatabaseAsync`) and Blazor endpoint mapping into `WebApplicationExtensions`. Map Blazor: `app.MapRazorComponents<App>().AddInteractiveServerRenderMode()` (match the companion's render mode).
- [ ] **Step 3:** Define `McpClientOptions` bound to a new `"McpClient"` section (move from the companion's `Mcp` shape: `Url`, `ApiKey`). Add an options registration in `Program.cs` and bind it. Leave the existing server `McpOptions`/`"Mcp"` untouched.
- [ ] **Step 4:** Make the MCP client lazy. Replace the companion's pre-`Build()` `await McpClient.CreateAsync(...)`/`ListToolsAsync()` with a singleton service that creates the client + caches tools on first use (or an `IHostedService` warmup running after the server is listening). Default `McpClient:Url` = `http://localhost:5101/mcp`. This avoids a startup self-call deadlock.

```csharp
// sketch: lazy provider resolved by the chat service
public sealed class McpToolsProvider(IOptions<McpClientOptions> opts)
{
    private Task<(McpClient client, IReadOnlyList<AITool> tools)>? _init;
    public Task<(McpClient, IReadOnlyList<AITool>)> GetAsync() =>
        _init ??= InitAsync(opts.Value);
    private static async Task<(McpClient, IReadOnlyList<AITool>)> InitAsync(McpClientOptions o)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(o.Url),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new() { ["X-Mcp-Api-Key"] = o.ApiKey },
        });
        var client = await McpClient.CreateAsync(transport);
        var tools = (await client.ListToolsAsync()).Cast<AITool>().ToList();
        return (client, tools);
    }
}
```
Wire `DndChatService` to resolve tools via `McpToolsProvider.GetAsync()` on first message.
- [ ] **Step 5:** Merge config into `Config/appsettings.json`: add `Ollama:Model` (`qwen3:8b`), `RateLimit` block, and a `McpClient` block (`Url`, `ApiKey`). Confirm `Ollama` section reconciles with the main app's existing `Ollama` options (the main app uses `Ollama:BaseUrl`; the companion used `Ollama:Url` + `Model` — unify to the main app's `OllamaOptions` shape and adjust the chat code to read it). Delete the companion's `Config/`.
- [ ] **Step 6:** Update `Program.cs` to call the new registrations and pipeline steps in order: existing API/MCP-server wiring + Blazor + auth + rate limit + chat. Enable antiforgery for the UI (`builder.Services.AddAntiforgery()` / `app.UseAntiforgery()` — currently commented in the main program) scoped so it does not break API/MCP routes.
- [ ] **Step 7:** Verify auth coexistence: cookie auth challenge redirects UI pages to login; `/admin` still requires `X-Admin-Api-Key`; `/mcp` still guarded by `McpAuthMiddleware`.
- [ ] **Step 8:** `dotnet build` — expect GREEN now. Commit: `feat: merge companion startup wiring into main host (lazy loopback MCP client)`.

---

## Task 5: Delete companion projects, move test files

**Files:**
- Delete: `DndMcpAICompanion/` (whole project), `DndMcpAICompanion.Tests/` (project)
- Move: companion test files → `DndMcpAICsharpFun.Tests/`

- [ ] **Step 1:** `git mv` the 7 companion test files into `DndMcpAICsharpFun.Tests/` under matching subfolders: `Auth/PasswordHasherTests.cs`, `Auth/UserRepositoryTests.cs`, `Campaign/CampaignRepositoryTests.cs`, `Campaign/CharacterSheetSerializationTests.cs`, `Campaign/HeroRepositoryTests.cs`, `Chat/ChatRateLimiterTests.cs`, `Chat/DndChatServiceTests.cs`. Fix namespaces/usings to `DndMcpAICsharpFun.*`. (The bodies are rewritten in Task 5b — here just relocate so they compile in the main test project's scope.)
- [ ] **Step 2:** Delete the `DndMcpAICompanion/` and `DndMcpAICompanion.Tests/` directories (csproj, `Program.cs`, `Dockerfile`, `Config/`, `data/companion.db`, `.dockerignore`, `*.lscache`, `bin/`, `obj/`). Remove any solution-file (`.sln`/`.slnx`) references if present.
- [ ] **Step 3:** `dotnet build` — the test project compiles (companion repo tests will still reference old ctors; if they block the build, mark them `[Fact(Skip="rewritten in 5b")]` temporarily). Commit: `refactor: delete companion projects; relocate test files`.

---

## Task 5b: Update & migrate the test suite

**Files:**
- Modify: `DndMcpAICsharpFun.Tests/Infrastructure/Tracking/TrackerFixture.cs` (and `SqliteIngestionTrackerTests.cs`)
- Modify: the main tests referencing the relocated types (8 files below)
- Rewrite: relocated companion repo tests (`Auth/UserRepositoryTests.cs`, `Campaign/CampaignRepositoryTests.cs`, `Campaign/HeroRepositoryTests.cs`)
- Port: `Auth/PasswordHasherTests.cs`, `Campaign/CharacterSheetSerializationTests.cs`, `Chat/ChatRateLimiterTests.cs`, `Chat/DndChatServiceTests.cs`
- Create: `DndMcpAICsharpFun.Tests/Persistence/AppDbContextSmokeTests.cs`

- [ ] **Step 1:** Update `TrackerFixture` to build `AppDbContext` instead of `IngestionDbContext`: change `DbContextOptions<IngestionDbContext>` → `DbContextOptions<AppDbContext>`, the `DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection)`, and `new AppDbContext(_options)`; keep the open-connection in-memory SQLite + `db.Database.Migrate()` pattern. This fixture is the shared seam for the ingestion-tracker tests — make `TestDb.NewContext()` (from Task 3b) reuse the same builder so there is one context-construction helper.
- [ ] **Step 2:** Fix the 8 main test files that reference the moved/renamed types — change `using DndMcpAICsharpFun.Infrastructure.Sqlite;` → `using DndMcpAICsharpFun.Domain;` (for `IngestionRecord`/`IngestionStatus`) and any `IngestionDbContext` usage → `AppDbContext`: `Infrastructure/Tracking/SqliteIngestionTrackerTests.cs`, `Ingestion/BlockIngestionOrchestratorTests.cs`, `Admin/BooksAdminEndpointsTests.cs`, `Entities/Deletion/EntityBookDeletionTests.cs`, `Entities/Admin/IngestEntitiesEndpointTests.cs`, `Entities/Admin/ExtractEntitiesEndpointTests.cs`, `Entities/Extraction/EntityExtractionOrchestratorTests.cs`, `Entities/Ingestion/EntityIngestionOrchestratorTests.cs`. Run `dotnet test` after this step; expect these to pass (behavior unchanged, only types relocated).
- [ ] **Step 3:** Rewrite the 3 relocated companion **repository** tests to use `AppDbContext` (open-connection in-memory SQLite via `TestDb.NewContext()`) instead of a `connectionString`/`SqliteConnection`. Drop any `InitializeAsync()` calls (schema now via migrations/`EnsureCreated`). Keep the same assertions (create→find→exists for users; create/get-all-by-user/delete for campaigns; create/snapshots/get-by-user for heroes). Un-skip anything skipped in Task 5 Step 3.
- [ ] **Step 4:** Port the other 4 relocated tests: `PasswordHasherTests` and `CharacterSheetSerializationTests` should pass with only namespace fixes; `ChatRateLimiterTests` likewise; `DndChatServiceTests` — update construction for the new lazy `McpToolsProvider` (inject a fake provider returning a fixed tool list / no tools) and the unified `OllamaOptions`. If chat now persists, add/extend a test asserting a turn is saved via `ChatRepository`.
- [ ] **Step 5:** Add `AppDbContextSmokeTests`: (a) `OnModelCreating` builds without error for all entities; (b) a fresh in-memory `AppDbContext` migrates and every `DbSet` is queryable; (c) a `HeroSnapshot` with a populated `CharacterSheet` round-trips through the JSON converter.
- [ ] **Step 6:** Confirm `coverlet.runsettings` / coverage exclusions still reference valid paths (the `Migrations/` and any `Infrastructure/Sqlite` exclusions may need repointing to `Infrastructure/Persistence`). Update `openspec/specs/coverage-exclusions` expectations only if the change archive later requires it — not here.
- [ ] **Step 7:** `dotnet test` — full suite green. Commit: `test: migrate suite to AppDbContext + EF repos; add persistence smoke tests`.

---

## Task 6: Docker

**Files:** `docker-compose.yml`, `docker-compose.prod.yml`, `Dockerfile`

- [ ] **Step 1:** `docker-compose.yml`: delete the `companion` service block and the `companion_data` named volume. On the `app` service add env `McpClient__Url=http://localhost:5101/mcp` and `McpClient__ApiKey=${MCP_API_KEY:-devMcpKey}` (loopback within the merged container). The UI is now served by `app` on 5101. `sqlite-web` stays (DB is still SQLite this change).
- [ ] **Step 2:** `docker-compose.prod.yml`: same companion-service/volume removal + `McpClient__*` env. Keep `books_data`/`app_data` as-is.
- [ ] **Step 3:** Confirm the root `Dockerfile` publishes the Blazor static web assets (Sdk.Web `dotnet publish` includes them by default); remove any companion-specific assumptions. Commit: `build: single app service serves API + MCP + UI`.

---

## Task 6b: Documentation

**Files:** `CLAUDE.md`, `README.md`, `DndMcpAICsharpFun.http`, `dnd-mcp-api.insomnia.json`

- [ ] **Step 1:** `CLAUDE.md`: rewrite the Architecture/Project-Overview section to describe one host serving API + MCP server + Blazor UI (remove the two-project framing). Update the **commands** (no separate companion run), the **Observability** services table (drop the `companion` row; `sqlite-web` still browses the unified DB), add a short **Persistence** note (one `AppDbContext`, EF Core/SQLite, chat history now persisted), and update any `Mcp`→`McpClient` config references.
- [ ] **Step 2:** `README.md`: update the project description, ports (single 5101; remove 5102), and run instructions to the single-host model.
- [ ] **Step 3:** `DndMcpAICsharpFun.http`: if the merge adds user-facing HTTP endpoints worth exercising (auth/campaign/hero APIs that aren't Blazor-only), add example requests; otherwise note the UI is Blazor-served. Apply the project rule: mirror every change into `dnd-mcp-api.insomnia.json` in the same commit.
- [ ] **Step 4:** Note in the change that the retired/modified companion capabilities (`companion-program-structure` removed; `mcp-client-integration` modified) are reconciled into `openspec/specs/` at archive time via `openspec archive merge-companion-into-main -y` (do NOT hand-edit `openspec/specs/` now). Commit: `docs: single-host architecture, persistence, and API contracts`.

---

## Task 7: Verification

- [ ] **Step 1:** `dotnet build` — 0 warnings, 0 errors (warnings-as-errors).
- [ ] **Step 2:** `dotnet test` — full merged suite green. Run: `dotnet test`. Expected: all pass; confirm the companion tests now run inside the main suite.
- [ ] **Step 3:** `docker compose up` — exactly one `app` service (no `companion`). Smoke: register → login → create campaign → add hero → open chat → assistant answers using MCP tools over loopback.
- [ ] **Step 4:** Confirm no `data/companion.db` is created and only the unified `AppDbContext` SQLite database exists.
- [ ] **Step 5:** Confirm chat turns persist: send messages, reopen the conversation, prior turns reload in order.

---

## Self-Review notes

- **Spec coverage:** `unified-app-host` → Tasks 1,4,5; `unified-persistence` → Tasks 3,3b (incl. chat persistence + `ChatMessage`); `domain-model-consolidation` → Task 2; `mcp-client-integration` (modified) → Task 4 Steps 3–4; `companion-program-structure` (removed) → Task 5. Test migration → Task 5b. Docker (`docker-stack` impl) → Task 6. Docs → Task 6b.
- **Test impact is fully enumerated:** `TrackerFixture` + 8 main test files (type relocation) + 3 companion repo tests (EF rewrite) + 4 ported companion tests + new `AppDbContextSmokeTests` — all in Task 5b.
- **Open risk carried from design:** Ollama config shape mismatch (`Ollama:Url`+`Model` vs main `Ollama:BaseUrl`) — reconciled in Task 4 Step 5; chat code + `DndChatServiceTests` must read the unified `OllamaOptions`.
- **Positional-record/EF caveat** noted in Task 3 Step 2 — convert `Hero`/`HeroSnapshot` to settable members if EF mapping requires it, preserving names.
