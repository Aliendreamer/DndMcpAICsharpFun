## Why

Change 1 unified all persistence behind a single EF Core `AppDbContext`, but kept it on SQLite as a deliberate stepping stone. SQLite is a single-file, single-writer store — fine for the merge, wrong for the companion's growing relational, multi-user data (users → campaigns → heroes → snapshots → chat) and for any real deployment. Now that the persistence layer is unified, swapping the engine to Postgres is the mechanical follow-up that was always the destination.

## What Changes

- **BREAKING**: The persistence provider changes from SQLite to **PostgreSQL** (Npgsql). The single `AppDbContext` now runs on Postgres; `UseSqlite(...)` becomes `UseNpgsql(...)` at the one registration point and the design-time factory. The model mapping (value converters, indexes, string-enum conversions, the `CharacterSheet` JSON column) is provider-neutral and carries over unchanged.
- Configuration moves from a SQLite file path (`Ingestion:DatabasePath`) to a Postgres connection (host/port/database/user/password), with compose env overrides following the existing pattern (dev defaults inline, prod via `${POSTGRES_PASSWORD}` etc.).
- The SQLite `InitialCreate` migration is replaced by a fresh Npgsql migration.
- **Data migration (one-off)**: a migrator copies the existing rows (the DMG + Tasha's `IngestionRecords`, plus any companion rows) from the old SQLite database into Postgres, so registrations carry over with no re-ingest. It is run once at cutover, then retired.
- **BREAKING**: Docker stack changes (both `docker-compose.yml` and `docker-compose.prod.yml`): add a **`postgres`** service (`postgres:18-alpine`, named volume, healthcheck), add **`pgAdmin`** (`dpage/pgadmin4`) at `:8080` replacing **`sqlite-web`**, and drop the `./data` DB mount (`data/` no longer holds the database; canonical + conversion-cache already live under `books/`). `app` gains `depends_on: postgres healthy`.
- **Tests**: persistence tests run against a **real Postgres** via **Testcontainers** (`Testcontainers.PostgreSql`), with **Respawn** resetting tables between tests. `TestDb` and `TrackerFixture` become Testcontainers-backed. Non-persistence tests are unaffected.

## Capabilities

### New Capabilities
- `postgres-persistence`: the application persists all relational data in PostgreSQL via Npgsql and the single `AppDbContext`, configured by a connection string, with EF migrations applied at startup.
- `sqlite-to-postgres-migration`: a one-off migrator copies existing rows from the legacy SQLite database into Postgres at cutover.
- `postgres-test-isolation`: persistence tests execute against a real Postgres instance provided by Testcontainers, isolated per test via Respawn.

### Modified Capabilities
- `data-browsers`: the SQLite Web UI (`coleifer/sqlite-web`) is replaced by **pgAdmin** (`dpage/pgadmin4`) for browsing the Postgres database at `http://localhost:8080`.
- `docker-stack`: the `sqlite-web` service is replaced by a `postgres` service plus a `pgAdmin` service; the `./data` database mount is removed.

## Impact

- **Dependencies**: add `Npgsql.EntityFrameworkCore.PostgreSQL` and (test) `Testcontainers.PostgreSql` + `Respawn` to CPM. Remove `Microsoft.EntityFrameworkCore.Sqlite` from the app once the migrator is retired; `Microsoft.Data.Sqlite` is retained only by the one-off migrator (then removed).
- **Modified**: `DndMcpAICsharpFun.csproj`, `Directory.Packages.props`, `Extensions/ServiceCollectionExtensions.cs` (registration), `Infrastructure/Persistence/AppDbContextDesignTimeFactory.cs`, `Infrastructure/Sqlite/IngestionOptions.cs` (DatabasePath → connection settings), `Config/appsettings*.json`, `docker-compose.yml`, `docker-compose.prod.yml`, `Migrations/**` (regenerated), the test fixtures (`TestDb`, `TrackerFixture`), `CLAUDE.md`, `README.md`.
- **Runtime**: Docker is now required to run the persistence tests. The app requires a reachable Postgres at startup.
- **Out of scope**: schema/behavior changes, `pgvector` (Qdrant owns vectors), any UI change, and the books/canonical layout (already settled in Change 0).
