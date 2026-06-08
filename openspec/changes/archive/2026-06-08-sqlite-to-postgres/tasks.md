## 1. Packages & provider swap

- [x] 1.1 Add to `Directory.Packages.props`: `Npgsql.EntityFrameworkCore.PostgreSQL`; (test) `Testcontainers.PostgreSql` and `Respawn`. Keep `Microsoft.EntityFrameworkCore.Sqlite` + `Microsoft.Data.Sqlite` for now (migrator only)
- [x] 1.2 In `DndMcpAICsharpFun.csproj` add the `Npgsql.EntityFrameworkCore.PostgreSQL` reference (leave SQLite refs until the migrator is retired in group 6)
- [x] 1.3 Replace `IngestionOptions.DatabasePath` (SQLite file) with Postgres connection settings — a `Postgres` options class (`Host`, `Port`, `Database`, `Username`, `Password`) or `ConnectionStrings:Postgres`; build a connection string helper
- [x] 1.4 In `Extensions/ServiceCollectionExtensions.cs` change `AddDbContextFactory<AppDbContext>` to `UseNpgsql(connectionString, o => o.EnableRetryOnFailure())`; keep the scoped `AppDbContext` delegate
- [x] 1.5 Update `Infrastructure/Persistence/AppDbContextDesignTimeFactory.cs` to `UseNpgsql` with a static design-time connection string
- [x] 1.6 Remove the SQLite-only `PRAGMA journal_mode=WAL` line from `MigrateDatabaseAsync` in `WebApplicationExtensions`

## 2. Migrations

- [x] 2.1 Delete `Migrations/**` (SQLite InitialCreate + snapshot)
- [x] 2.2 Generate a fresh Npgsql migration: `dotnet ef migrations add InitialCreate -c AppDbContext -o Migrations` (requires a design-time connection; the factory provides it)
- [x] 2.3 Inspect the generated migration: all tables present, `CharacterJson` column, unique index on `Users.Username`, string-converted enums, identity keys

## 3. Config & docker

- [x] 3.1 `Config/appsettings.json`: replace `Ingestion:DatabasePath` with the `Postgres` section (host/port/db/user, no committed password); set sensible container defaults
- [x] 3.2 `Config/appsettings.Development.json`: local-run Postgres host (`localhost`) override, matching the Marker localhost pattern
- [x] 3.3 `docker-compose.yml`: add `postgres` (`postgres:18-alpine`, `POSTGRES_DB/USER/PASSWORD`, `postgres_data` volume, `pg_isready` healthcheck); add `pgadmin` (`dpage/pgadmin4`, `PGADMIN_DEFAULT_EMAIL/PASSWORD`, port 8080, depends_on postgres); remove `sqlite-web`, the `./data` mount, and `Ingestion__DatabasePath`; add `Postgres__*` env + `depends_on: postgres healthy` to `app`; add the `postgres_data` volume, drop unused volumes
- [x] 3.4 `docker-compose.prod.yml`: same service changes; replace `app_data` with `postgres_data`; prod password via `${POSTGRES_PASSWORD}`
- [x] 3.5 Validate both composes (`docker compose config`)

## 4. One-off data migrator

- [x] 4.1 Create `Tools/SqliteToPostgres` console: build two `AppDbContext`s (`UseSqlite(oldFile)`, `UseNpgsql(target)`); copy each `DbSet` (`IngestionRecords`, `Users`, `Campaigns`, `Heroes`, `HeroSnapshots`, `ChatTurns`)
- [x] 4.2 Preserve explicit primary keys into Postgres identity columns (identity-insert / `OVERRIDING SYSTEM VALUE`), then reset each sequence to `max(id)`
- [x] 4.3 Assert per-table source/target row counts match; log a summary
- [x] 4.4 Run it against the live SQLite `data/ingestion.db` → the new Postgres; confirm DMG + Tasha's `IngestionRecords` present with correct fields (do NOT delete the SQLite file — rollback safety)

## 5. Tests on Testcontainers + Respawn

- [x] 5.1 Add a `PostgresFixture` (`ICollectionFixture`) that starts one `postgres:18-alpine` container and exposes its connection string; apply migrations/schema on init
- [x] 5.2 Rewrite `TestDb` to build `AppDbContext` against the fixture container instead of SQLite in-memory; add a `Respawner` that resets all tables
- [x] 5.3 Update `TrackerFixture` to use the Postgres container; reset between tests via Respawn
- [x] 5.4 Put the persistence test classes (`UserRepositoryTests`, `CampaignRepositoryTests`, `HeroRepositoryTests`, `AppDbContextSmokeTests`, the ingestion-tracker tests) into the `postgres` collection; ensure non-persistence tests are untouched
- [x] 5.5 `dotnet test` — full suite green (Docker running); confirm the correlated hero-count subquery, the JSON round-trip, and `ExecuteDelete` cascades pass on real Postgres

## 6. Retire SQLite from the app + docs

- [x] 6.1 Remove `Microsoft.EntityFrameworkCore.Sqlite` (and `Microsoft.Data.Sqlite` if unused) from the app project; keep them only on `Tools/SqliteToPostgres`
- [x] 6.2 `CLAUDE.md`: update the Persistence section (Postgres, connection string, pgAdmin, Testcontainers) and the observability table (sqlite-web → pgAdmin)
- [x] 6.3 `README.md`: update the architecture diagram (SQLite box → Postgres), prerequisites (Docker for tests), and the data-browser entry
- [x] 6.4 No HTTP endpoints change — `.http`/insomnia unaffected (verify)

## 7. Verification

- [x] 7.1 `dotnet build` — 0 warnings / 0 errors (warnings-as-errors)
- [x] 7.2 `dotnet test` — full suite green against Testcontainers Postgres
- [x] 7.3 `docker compose up` — `postgres` healthy, `app` healthy, migrations applied; `pgAdmin` reachable at `:8080`; no `sqlite-web`
- [x] 7.4 Confirm the migrated DMG + Tasha's registrations are present via `GET /admin/books`
- [x] 7.5 Confirm no SQLite database file or `./data` DB mount is used by the running stack
