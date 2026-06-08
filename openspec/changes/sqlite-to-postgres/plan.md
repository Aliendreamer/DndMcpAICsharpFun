# SQLite → Postgres Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax. The canonical checklist + acceptance criteria are in `tasks.md`; this file adds exact code, paths, and commands.

**Goal:** Run the single `AppDbContext` on PostgreSQL (Npgsql) instead of SQLite, migrate the existing rows over once, swap `sqlite-web` for `postgres` + `pgAdmin` in Docker, and run persistence tests against real Postgres via Testcontainers + Respawn.

**Architecture:** Provider swap only — the EF model is unchanged. `UseSqlite` → `UseNpgsql` at one registration point + the design-time factory; DB path config → connection string. A one-off `Tools/SqliteToPostgres` console copies rows EF-to-EF. Tests get a shared Postgres container (collection fixture) reset by Respawn.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, PostgreSQL 18, Testcontainers.PostgreSql, Respawn, dpage/pgadmin4, xUnit/FluentAssertions. CPM; warnings-as-errors.

**Conventions:** All `.cs` reads/edits go through Serena (project rule). Commit after each task group; do NOT push. Verify versions restore before pinning.

---

## File Structure

| Path | Responsibility |
| --- | --- |
| `Directory.Packages.props` | Add Npgsql + Testcontainers.PostgreSql + Respawn versions |
| `Infrastructure/Postgres/PostgresOptions.cs` (new) | Connection settings + connection-string builder |
| `Extensions/ServiceCollectionExtensions.cs` | `UseNpgsql` registration |
| `Infrastructure/Persistence/AppDbContextDesignTimeFactory.cs` | `UseNpgsql` design-time |
| `Extensions/WebApplicationExtensions.cs` | drop SQLite `PRAGMA` |
| `Migrations/**` | regenerated for Npgsql |
| `Tools/SqliteToPostgres/**` (new) | one-off data migrator |
| `docker-compose.yml`, `docker-compose.prod.yml` | postgres + pgAdmin, drop sqlite-web |
| `DndMcpAICsharpFun.Tests/Persistence/PostgresFixture.cs` (new), `TestDb.cs`, `Infrastructure/Tracking/TrackerFixture.cs` | Testcontainers + Respawn |

---

## Task 1: Packages + Postgres options

**Files:** `Directory.Packages.props`, `DndMcpAICsharpFun.csproj`, `Infrastructure/Postgres/PostgresOptions.cs`, `Infrastructure/Sqlite/IngestionOptions.cs`

- [ ] **Step 1:** Add versions to `Directory.Packages.props` (verify each restores against EF Core 10 first with `dotnet restore`):

```xml
<PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
<PackageVersion Include="Testcontainers.PostgreSql" Version="4.0.0" />
<PackageVersion Include="Respawn" Version="6.2.1" />
```
If a version fails to restore, use the latest stable that targets EF Core 10 / net10.0.

- [ ] **Step 2:** In `DndMcpAICsharpFun.csproj` add `<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />`. Leave `Microsoft.EntityFrameworkCore.Sqlite` and `Microsoft.Data.Sqlite` (migrator needs them; removed in Task 6).
- [ ] **Step 3:** Create `Infrastructure/Postgres/PostgresOptions.cs`:

```csharp
namespace DndMcpAICsharpFun.Infrastructure.Postgres;

public sealed class PostgresOptions
{
    public string Host { get; set; } = "postgres";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "dnd";
    public string Username { get; set; } = "dnd";
    public string Password { get; set; } = "dnd";

    public string ConnectionString() =>
        $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}";
}
```

- [ ] **Step 4:** Remove `DatabasePath` from `Infrastructure/Sqlite/IngestionOptions.cs` (keep `BooksPath`, `MaxChunkTokens`, `OverlapTokens`). Search for other `DatabasePath` references with Serena `find_referencing_symbols` and fix.
- [ ] **Step 5:** `dotnet build` — expect failure at the `UseSqlite` registration (fixed next task). Commit: `build: add Npgsql/Testcontainers/Respawn + PostgresOptions`.

---

## Task 2: Provider swap (registration + design-time)

**Files:** `Extensions/ServiceCollectionExtensions.cs`, `Infrastructure/Persistence/AppDbContextDesignTimeFactory.cs`, `Extensions/WebApplicationExtensions.cs`, `Program.cs`

- [ ] **Step 1:** Bind `PostgresOptions` in `Program.cs` options block:

```csharp
builder.Services.AddOptions<DndMcpAICsharpFun.Infrastructure.Postgres.PostgresOptions>()
    .BindConfiguration("Postgres")
    .ValidateOnStart();
```

- [ ] **Step 2:** In `ServiceCollectionExtensions.cs` replace the `AddDbContextFactory<AppDbContext>` body:

```csharp
services.AddDbContextFactory<AppDbContext>(static (sp, options) =>
{
    var pg = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
    options.UseNpgsql(pg.ConnectionString(), o => o.EnableRetryOnFailure());
});
services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
```
Add `using DndMcpAICsharpFun.Infrastructure.Postgres;` (and keep `Microsoft.EntityFrameworkCore`).

- [ ] **Step 3:** Update `AppDbContextDesignTimeFactory.cs`:

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql("Host=localhost;Port=5432;Database=dnd;Username=dnd;Password=dnd")
    .Options;
```

- [ ] **Step 4:** In `WebApplicationExtensions.MigrateDatabaseAsync`, delete the SQLite-only lines:

```csharp
// REMOVE: await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
```
Keep `await db.Database.MigrateAsync();`.

- [ ] **Step 5:** `dotnet build` — green (migrations still SQLite-shaped but compile). Commit: `refactor: register AppDbContext on Npgsql`.

---

## Task 3: Regenerate migration for Npgsql

**Files:** `Migrations/**`

- [ ] **Step 1:** Delete the SQLite migrations: `git rm Migrations/*.cs`.
- [ ] **Step 2:** Generate the Npgsql migration:

```bash
dotnet ef migrations add InitialCreate -c AppDbContext -o Migrations --project DndMcpAICsharpFun.csproj
```
Expected: `Done.` and a new `*_InitialCreate.cs` using `npgsql:ValueGenerationStrategy` identity columns.

- [ ] **Step 3:** Verify the migration: 6 tables, `CharacterJson` column on `HeroSnapshots`, unique index `IX_Users_Username`, `text` columns for the string-converted enums.

```bash
grep -E 'CreateTable|CharacterJson|IX_Users_Username|Identity' Migrations/*_InitialCreate.cs | head
```

- [ ] **Step 4:** `dotnet build` green. Commit: `refactor: regenerate InitialCreate for Npgsql`.

---

## Task 4: Docker — postgres + pgAdmin

**Files:** `docker-compose.yml`, `docker-compose.prod.yml`, `Config/appsettings.json`, `Config/appsettings.Development.json`

- [ ] **Step 1:** `docker-compose.yml` — remove the `sqlite-web` service and the `./data:/app/data` mount on `app`. Add to `app.environment`:

```yaml
      - Postgres__Host=postgres
      - Postgres__Password=${POSTGRES_PASSWORD:-dnd}
```
Add to `app.depends_on`:

```yaml
      postgres:
        condition: service_healthy
```

- [ ] **Step 2:** Add the `postgres` + `pgadmin` services to `docker-compose.yml`:

```yaml
  postgres:
    image: postgres:18-alpine
    environment:
      - POSTGRES_DB=dnd
      - POSTGRES_USER=dnd
      - POSTGRES_PASSWORD=${POSTGRES_PASSWORD:-dnd}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U dnd -d dnd"]
      interval: 10s
      timeout: 5s
      retries: 5
    networks:
      - dnd_net
    restart: unless-stopped

  pgadmin:
    image: dpage/pgadmin4:latest
    environment:
      - PGADMIN_DEFAULT_EMAIL=admin@dnd.local
      - PGADMIN_DEFAULT_PASSWORD=${PGADMIN_PASSWORD:-admin}
      - PGADMIN_CONFIG_SERVER_MODE=False
    ports:
      - "8080:80"
    depends_on:
      - postgres
    networks:
      - dnd_net
    restart: unless-stopped
```

- [ ] **Step 3:** In the `volumes:` block of `docker-compose.yml`, add `postgres_data:` and remove any now-unused volume; `data/` no longer holds the DB.
- [ ] **Step 4:** `docker-compose.prod.yml` — same: drop `sqlite-web`, add `postgres` (password `${POSTGRES_PASSWORD}` required, no default) + `pgadmin`, replace the `app_data:/data` volume with `postgres_data`, set `Postgres__*` env on `app`, `depends_on: postgres healthy`. Remove `Ingestion__DatabasePath`.
- [ ] **Step 5:** `Config/appsettings.json` — remove `Ingestion:DatabasePath`; add:

```json
  "Postgres": {
    "Host": "postgres",
    "Port": 5432,
    "Database": "dnd",
    "Username": "dnd",
    "Password": "dnd"
  },
```
`Config/appsettings.Development.json` — add `"Postgres": { "Host": "localhost" }` for local `dotnet run` (mirrors the Marker localhost override).

- [ ] **Step 6:** Validate: `docker compose -f docker-compose.yml config` and `-f docker-compose.prod.yml config` both succeed. Commit: `build: postgres + pgAdmin services; drop sqlite-web`.

---

## Task 5: One-off data migrator

**Files:** `Tools/SqliteToPostgres/SqliteToPostgres.csproj`, `Tools/SqliteToPostgres/Program.cs`

- [ ] **Step 1:** Create `Tools/SqliteToPostgres/SqliteToPostgres.csproj` (references the app project for `AppDbContext`/`Domain`, plus both EF providers):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\DndMcpAICsharpFun.csproj" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>
</Project>
```
(`Tools/**` is already excluded from the app csproj.)

- [ ] **Step 2:** `Tools/SqliteToPostgres/Program.cs` — copy every set, preserving ids, then reset sequences:

```csharp
using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var sqlitePath = args.ElementAtOrDefault(0) ?? "data/ingestion.db";
var pgConn = args.ElementAtOrDefault(1) ?? "Host=localhost;Port=5432;Database=dnd;Username=dnd;Password=dnd";

AppDbContext Sqlite() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={sqlitePath}").Options);
AppDbContext Pg() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pgConn).Options);

await using var pgInit = Pg();
await pgInit.Database.MigrateAsync();

await using var src = Sqlite();
await using var dst = Pg();

// Copy in FK-free order (no relationships are configured; any order works).
dst.IngestionRecords.AddRange(await src.IngestionRecords.AsNoTracking().ToListAsync());
dst.Users.AddRange(await src.Users.AsNoTracking().ToListAsync());
dst.Campaigns.AddRange(await src.Campaigns.AsNoTracking().ToListAsync());
dst.Heroes.AddRange(await src.Heroes.AsNoTracking().ToListAsync());
dst.HeroSnapshots.AddRange(await src.HeroSnapshots.AsNoTracking().ToListAsync());
dst.ChatTurns.AddRange(await src.ChatTurns.AsNoTracking().ToListAsync());

// Allow explicit ids into identity columns for this bulk insert.
await dst.Database.OpenConnectionAsync();
await dst.SaveChangesAsync();

// Reset each identity sequence to MAX(id) so future inserts don't collide.
foreach (var (table, col) in new[] {
    ("IngestionRecords","Id"),("Users","Id"),("Campaigns","Id"),
    ("Heroes","Id"),("HeroSnapshots","Id"),("ChatTurns","Id") })
{
    await dst.Database.ExecuteSqlRawAsync(
        $"SELECT setval(pg_get_serial_sequence('\"{table}\"','{col}'), " +
        $"COALESCE((SELECT MAX(\"{col}\") FROM \"{table}\"), 1));");
}

// Verify counts.
Console.WriteLine($"IngestionRecords: {await src.IngestionRecords.CountAsync()} -> {await dst.IngestionRecords.CountAsync()}");
```

> NOTE: EF inserting explicit ids into Npgsql `GENERATED BY DEFAULT AS IDENTITY` works (BY DEFAULT accepts supplied values); the migration uses the default identity strategy. If the generated migration used `GENERATED ALWAYS`, change it to `BY DEFAULT` (EF default for Npgsql is BY DEFAULT). The `setval` reset is the critical post-step — verify `nextval` > max id.

- [ ] **Step 3:** Bring up just Postgres: `docker compose up -d postgres`. Run the migrator against the live SQLite file:

```bash
dotnet run --project Tools/SqliteToPostgres -- data/ingestion.db "Host=localhost;Port=5432;Database=dnd;Username=dnd;Password=dnd"
```
Expected: `IngestionRecords: 2 -> 2`. Do NOT delete `data/ingestion.db`.

- [ ] **Step 4:** Commit: `feat: one-off SQLite→Postgres data migrator`.

---

## Task 6: Tests on Testcontainers + Respawn

**Files:** `DndMcpAICsharpFun.Tests/Persistence/PostgresFixture.cs` (new), `Persistence/TestDb.cs`, `Infrastructure/Tracking/TrackerFixture.cs`, persistence test classes

- [ ] **Step 1:** Create `Persistence/PostgresFixture.cs`:

```csharp
using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Persistence;

public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } =
        new PostgreSqlBuilder().WithImage("postgres:18-alpine").Build();

    private Respawner _respawner = null!;

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await using var conn = new Npgsql.NpgsqlConnection(Container.GetConnectionString());
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new("__EFMigrationsHistory")],
        });
    }

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Container.GetConnectionString()).Options);

    public async Task ResetAsync()
    {
        await using var conn = new Npgsql.NpgsqlConnection(Container.GetConnectionString());
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

- [ ] **Step 2:** Rewrite `Persistence/TestDb.cs` to delegate to the fixture (an `IDbContextFactory<AppDbContext>` over the container):

```csharp
using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Tests.Persistence;

public sealed class TestDb(PostgresFixture pg) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => pg.NewContext();
}
```

- [ ] **Step 3:** Put each persistence test class in the collection and reset before each test. Example for `CampaignRepositoryTests`:

```csharp
[Collection("postgres")]
public sealed class CampaignRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly TestDb _db;
    private readonly CampaignRepository _repo;
    private readonly HeroRepository _heroes;

    public CampaignRepositoryTests(PostgresFixture pg)
    {
        _pg = pg; _db = new TestDb(pg);
        _repo = new CampaignRepository(_db);
        _heroes = new HeroRepository(_db);
    }

    public Task InitializeAsync() => _pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ... existing [Fact] bodies unchanged ...
}
```
Apply the same `[Collection("postgres")]` + ctor-injected `PostgresFixture` + `ResetAsync()` pattern to `UserRepositoryTests`, `HeroRepositoryTests`, `AppDbContextSmokeTests`.

- [ ] **Step 4:** Update `Infrastructure/Tracking/TrackerFixture.cs` to build `AppDbContext`/`SqliteIngestionTracker` over the Postgres container (inject `PostgresFixture`; the tracker tests join the `postgres` collection and `ResetAsync()` per test). Remove the SQLite in-memory connection.
- [ ] **Step 5:** Run the persistence subset (Docker must be running):

```bash
dotnet test --filter 'FullyQualifiedName~Auth|FullyQualifiedName~Campaign|FullyQualifiedName~Chat|FullyQualifiedName~Persistence|FullyQualifiedName~Tracking'
```
Expected: all pass against real Postgres.

- [ ] **Step 6:** Full suite: `dotnet test`. Expected green. Commit: `test: persistence tests on Testcontainers Postgres + Respawn`.

---

## Task 7: Retire SQLite from the app + docs

**Files:** `DndMcpAICsharpFun.csproj`, `CLAUDE.md`, `README.md`

- [ ] **Step 1:** Remove `Microsoft.EntityFrameworkCore.Sqlite` and `Microsoft.Data.Sqlite` `PackageReference`s from `DndMcpAICsharpFun.csproj` (they remain on `Tools/SqliteToPostgres`). `dotnet build` — green (confirms the app no longer needs SQLite).
- [ ] **Step 2:** `CLAUDE.md` — update the Persistence section (Postgres via Npgsql, connection string config, EF migrations, Testcontainers tests) and the observability table (`sqlite-web` → `pgAdmin :8080`).
- [ ] **Step 3:** `README.md` — architecture diagram SQLite box → `Postgres :5432`; data-browser row → pgAdmin; Prerequisites note "Docker required to run the persistence tests".
- [ ] **Step 4:** Commit: `docs: Postgres persistence + pgAdmin + Testcontainers`.

---

## Task 8: Verification

- [ ] **Step 1:** `dotnet build` — 0 warnings / 0 errors.
- [ ] **Step 2:** `dotnet test` — full suite green (Docker up).
- [ ] **Step 3:** `docker compose up -d --build` — `postgres` healthy, `app` healthy (`curl -s -o /dev/null -w '%{http_code}' http://localhost:5101/health` → 200), `pgAdmin` at `:8080`, no `sqlite-web`.
- [ ] **Step 4:** `curl -s -H "X-Admin-Api-Key: devXXXdev" http://localhost:5101/admin/books` — DMG + Tasha's present (migrated).
- [ ] **Step 5:** Confirm no `data/ingestion.db` is read by the running stack and no `./data` DB mount exists.

---

## Self-Review notes

- **Spec coverage:** `postgres-persistence` → Tasks 1,2,3,4; `sqlite-to-postgres-migration` → Task 5 (+ retire in 7); `postgres-test-isolation` → Task 6; `data-browsers`/`docker-stack` (modified) → Task 4.
- **Identity-insert risk** (the spec's flagged gotcha) → Task 5 Step 2 NOTE: Npgsql default identity is `BY DEFAULT` (accepts explicit ids); the `setval` sequence reset is the required post-step, verified by counts + a new insert.
- **Version risk:** Task 1 Step 1 says verify each package version restores against EF Core 10 before pinning — adjust if `10.0.0`/`4.0.0`/`6.2.1` don't resolve.
- **Docker-for-tests:** Task 6 scopes Testcontainers to the `postgres` collection; non-persistence tests stay Docker-free.
