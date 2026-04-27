## Why

The LLM extraction pipeline (`/extract` + `/ingest-json`) is the intended ingestion path. The old background service auto-fires the legacy chunking pipeline on every book registration, wasting resources and conflicting with the explicit, operator-controlled workflow. Registration should save the file and nothing more.

## What Changes

- **BREAKING** `POST /admin/books/register` no longer auto-starts ingestion; book remains `Pending` until an operator explicitly calls `/reingest`, `/extract`, or `/ingest-json`.
- **BREAKING** `POST /admin/books/register-path` same — file is saved and tracked; no pipeline fires automatically.
- Remove `IngestionBackgroundService` (the `IHostedService` that polls for `Pending` records and runs the standard chunking pipeline).
- Remove `IServiceScopeFactory` injection and the `Task.Run` fire-and-forget blocks from both `RegisterBook` and `RegisterBookByPath` handlers.
- Remove DI registration of `IngestionBackgroundService`.
- Remove the background service test class `IngestionBackgroundServiceTests`.

## Capabilities

### New Capabilities

*(none — this change removes a capability; it does not introduce new ones)*

### Modified Capabilities

- `ingestion-pipeline`: Requirement change — book registration no longer auto-starts the standard ingestion pipeline. The pipeline is now exclusively operator-triggered via `/reingest` (legacy chunking path) or `/extract` + `/ingest-json` (LLM extraction path).

## Impact

- **`Features/Ingestion/IngestionBackgroundService.cs`** — deleted
- **`Features/Admin/BooksAdminEndpoints.cs`** — `RegisterBook` and `RegisterBookByPath` lose `IServiceScopeFactory` param and `Task.Run` blocks
- **`Extensions/ServiceCollectionExtensions.cs`** — `AddHostedService<IngestionBackgroundService>()` call removed
- **`DndMcpAICsharpFun.Tests/Ingestion/IngestionBackgroundServiceTests.cs`** — deleted
- **`openspec/specs/ingestion-pipeline/spec.md`** — delta: registration no longer triggers ingestion
- No API surface changes to endpoints, request/response shapes, or authentication
- No changes to Qdrant, Ollama, or SQLite integrations
