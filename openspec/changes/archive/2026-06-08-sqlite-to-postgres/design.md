## Context

After Change 1 (`merge-companion-into-main`), all relational data lives behind one EF Core `AppDbContext` on SQLite:

- Registration: `Extensions/ServiceCollectionExtensions.cs` → `AddDbContextFactory<AppDbContext>(... UseSqlite($"Data Source={IngestionOptions.DatabasePath}"))` plus a scoped `AppDbContext` delegate.
- Repositories (`UserRepository`, `CampaignRepository`, `HeroRepository`, `ChatRepository`) use `IDbContextFactory<AppDbContext>` (short-lived contexts). The ingestion tracker uses the scoped `AppDbContext`.
- Mapping in `OnModelCreating` is provider-neutral: indexes, `HasConversion<string>()` for enums, and a JSON value-converter for `HeroSnapshot.CharacterSheet`.
- Tests use a SQLite in-memory `TestDb` factory and a `TrackerFixture`.
- Docker: `sqlite-web` browses `/data/ingestion.db`; the app mounts `./data` (dev) / `app_data` (prod).

The DMG + Tasha's registrations were just re-ingested into the SQLite DB; the user wants them preserved. `dnd_blocks`/`dnd_entities` live in Qdrant and are unaffected.

## Goals / Non-Goals

**Goals:**
- Run the single `AppDbContext` on PostgreSQL via Npgsql, configured by a connection string.
- Preserve existing rows via a one-off SQLite→Postgres data copy (no re-ingest).
- One `postgres` service + `pgAdmin` in both composes; `sqlite-web` and the `./data` DB mount removed.
- Persistence tests run against real Postgres (Testcontainers) with Respawn isolation; all tests stay green.
- Behavior parity — no schema or API change.

**Non-Goals:**
- `pgvector` or moving vectors out of Qdrant.
- Any schema, repository-API, or UI/behavior change.
- Multi-tenant/connection-pool tuning beyond defaults.
- Changes to the books/canonical layout (Change 0) or to Qdrant.

## Decisions

### D1: Npgsql provider, connection string config
Swap `Microsoft.EntityFrameworkCore.Sqlite` → `Npgsql.EntityFrameworkCore.PostgreSQL`. Replace `IngestionOptions.DatabasePath` with Postgres connection settings bound from config (`Host`, `Port`, `Database`, `Username`, `Password`) or a single `ConnectionStrings:Postgres`. The registration builds `UseNpgsql(connectionString)`; the design-time factory uses a static local connection string. Alternative (keep SQLite as a fallback/dual-provider) rejected — YAGNI; the goal is one engine.

### D2: Fresh Npgsql migration
The SQLite `InitialCreate` emits SQLite DDL and cannot apply to Postgres. Delete `Migrations/**` and regenerate a single `InitialCreate` for the Npgsql provider. `MigrateDatabaseAsync` (already in `WebApplicationExtensions`) applies it at startup; the `PRAGMA journal_mode=WAL` line (SQLite-only) is removed.

### D3: One-off data migrator, then retire SQLite
A small migrator (`Tools/SqliteToPostgres` console, or an opt-in startup path gated by an env flag) builds two `AppDbContext`s — one `UseSqlite(old file)`, one `UseNpgsql(target)` — reads every entity set, and `AddRange` + `SaveChanges` into Postgres (identity insert preserved where needed). It runs once at cutover. `Microsoft.Data.Sqlite` + EF Sqlite are kept *only* for this tool and removed when it is retired. Alternative (`pg_dump`/CSV) rejected: schemas differ slightly per provider and the EF-to-EF copy is type-safe and uses the same model.

> Identity columns: Postgres `GENERATED ... AS IDENTITY` will not accept explicit ids by default. The migrator must enable identity-insert (e.g. `OVERRIDING SYSTEM VALUE` / temporarily set the column to not-generated, or copy with `IDENTITY_INSERT`-equivalent) and then reset the sequence to `max(id)`. This is the one fiddly part and is covered explicitly in tasks.

### D4: Compose — postgres + pgAdmin, drop sqlite-web
Add `postgres` (`postgres:18-alpine`, `POSTGRES_DB/USER/PASSWORD` env, `postgres_data` volume, `pg_isready` healthcheck). `app` gains `depends_on: postgres: condition: service_healthy` and its connection env. Add `pgadmin` (`dpage/pgadmin4`, `PGADMIN_DEFAULT_EMAIL/PASSWORD`, port `8080`, `depends_on: postgres`). Remove `sqlite-web`, the `./data` mount, `companion_data` is already gone, and `Ingestion__DatabasePath`. Prod mirrors this; the `app_data` volume is replaced by `postgres_data`.

### D5: Tests on Testcontainers + Respawn
A shared `PostgresFixture` (xUnit `ICollectionFixture`) starts one `postgres:18-alpine` container for the persistence-test collection. `TestDb` becomes a factory pointed at the container; `TrackerFixture` adopts it. A `Respawner` resets all tables between tests (fast, no per-test container). Persistence tests join the `postgres` collection; the ~456 non-persistence tests are untouched and need no Docker.

## Risks / Trade-offs

- **Identity-insert during migration** → wrong/duplicate keys or broken sequences. **Mitigation:** D3 — explicit override + sequence reset; a post-migration assertion that row counts match and `nextval > max(id)`.
- **Provider SQL differences surface only at runtime** (correlated subquery, `jsonb`, `ExecuteDelete`). **Mitigation:** Testcontainers runs the real provider — exactly the gap SQLite tests would hide.
- **Docker now required for persistence tests** → CI/local friction. **Mitigation:** scope Testcontainers to the persistence collection; document the requirement; non-persistence tests stay Docker-free.
- **Startup ordering** (app before Postgres ready) → migration failure. **Mitigation:** `depends_on: service_healthy` + `pg_isready`; EF retry-on-failure (`UseNpgsql(o => o.EnableRetryOnFailure())`).
- **Secrets** (Postgres password) → leakage. **Mitigation:** dev defaults inline (like `Admin__ApiKey`); prod via `${POSTGRES_PASSWORD}` env / git-crypt, never committed.

## Migration Plan

1. Add Npgsql (and test) packages to CPM; swap the app provider + registration + design-time factory; replace `IngestionOptions` DB settings.
2. Delete SQLite migrations; generate the Npgsql `InitialCreate`.
3. Build the one-off migrator; run it against the current SQLite file → Postgres; verify counts + sequences.
4. Update both composes (postgres + pgAdmin, drop sqlite-web/`./data`); bring the stack up; confirm app healthy and the 2 registrations present.
5. Convert `TestDb`/`TrackerFixture` to Testcontainers + Respawn; green the full suite.
6. Retire the migrator's SQLite dependency from the app; update docs.

**Rollback:** revert the change branch; the SQLite DB file and `sqlite-web` return intact (the old `data/ingestion.db` is not deleted by the migrator).

## Open Questions

- Where the migrator lives — a `Tools/` console run manually at cutover (preferred, keeps it out of the app) vs an env-gated startup path. Resolve in tasks; default to a `Tools/` console.
