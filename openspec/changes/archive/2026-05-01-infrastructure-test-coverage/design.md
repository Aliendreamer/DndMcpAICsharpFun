## Context

The project uses xUnit + NSubstitute throughout `DndMcpAICsharpFun.Tests/`. Three critical classes have 0% coverage: `SqliteIngestionTracker` (EF Core + SQLite), `OllamaLlmClassifier` (LLM-backed classifier), and `BooksAdminEndpoints` (Minimal API admin routes, currently 12.9%). Tests are split into 3 phases, each self-contained.

## Goals / Non-Goals

**Goals:**
- Cover all `IIngestionTracker` methods with real EF Core + SQLite roundtrips (no mocks for the DB layer)
- Cover `OllamaLlmClassifier` happy path, empty/invalid responses, and cancellation with a mocked LLM client
- Cover all `BooksAdminEndpoints` routes at the HTTP level with mocked external services

**Non-Goals:**
- Testcontainers for Qdrant or Ollama (deferred to a future phase)
- Production code changes
- 100% branch coverage of every edge case in untested files not listed above

## Decisions

**Phase 1 — in-memory SQLite over temp-file SQLite**
EF Core's `UseSqlite("DataSource=:memory:")` with a shared `SqliteConnection` kept open for the test lifetime gives full schema isolation per test at zero I/O cost. A temp-file approach would add cleanup logic with no benefit for these simple CRUD operations. Fallback to temp file only if EF migrations fail with `:memory:`.

**Phase 3 — `WebApplicationFactory` over direct handler invocation**
The admin endpoints use model binding, route parameters, and `IFormFile` — all of which require the ASP.NET pipeline. Calling the private static methods directly would bypass routing and `DisableAntiforgery()` behavior. `WebApplicationFactory` exercises the real pipeline while allowing `ConfigureTestServices` to swap in mocks.

**Phase 3 — bypass `AdminApiKeyMiddleware` via `Use` passthrough**
Tests focus on endpoint logic, not auth. The middleware is replaced in `ConfigureWebHost` with `app.Use((ctx, next) => next(ctx))` so all requests pass through without an API key.

**Shared `SqliteConnection` per test class**
Each test class that uses `TrackerFixture` opens one `SqliteConnection` in the constructor and disposes it in `Dispose()`. EF Core's `MigrateAsync()` runs once per connection. Individual tests create a fresh `IngestionDbContext` over the same open connection — fast and isolated because `:memory:` databases are connection-scoped.

## Risks / Trade-offs

- **EF Core migration state**: `MigrateAsync()` on `:memory:` requires all pending migrations to apply cleanly. If migrations are broken, tests will fail at setup, not at assertion — which is actually desirable (catches migration regressions). → No mitigation needed.
- **`WebApplicationFactory` startup cost**: Spins up the full ASP.NET host once per test class (via `IClassFixture`). Slow if many fixture classes. → Use `IClassFixture<BooksAdminFactory>` so the host is shared across all endpoint tests.
- **`IOllamaApiClient` streaming interface**: The classifier reads streamed tokens. The mock must return an `IAsyncEnumerable<string>` that yields the full JSON string. → Use `NSubstitute` with a helper that yields from a list.
