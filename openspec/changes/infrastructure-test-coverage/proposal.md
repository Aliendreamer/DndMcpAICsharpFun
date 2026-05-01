## Why

Line coverage sits at 41.5% with several infrastructure and endpoint classes at 0%. The DB tracking layer, LLM classifier, and admin endpoints are core to the pipeline but completely untested, making regressions invisible.

## What Changes

- Add unit tests for `SqliteIngestionTracker` using in-memory SQLite (EF Core + real roundtrips)
- Add unit tests for `OllamaLlmClassifier` using a mocked `IOllamaApiClient`
- Add integration tests for `BooksAdminEndpoints` using `WebApplicationFactory` with middleware bypassed and all external services mocked

## Capabilities

### New Capabilities

- `sqlite-ingestion-tracker-tests`: Full test coverage of `IIngestionTracker` methods via in-memory SQLite
- `ollama-llm-classifier-tests`: Unit tests for `OllamaLlmClassifier` happy path, empty/invalid responses, and cancellation
- `books-admin-endpoints-tests`: HTTP-level integration tests for all admin book endpoints via `WebApplicationFactory`

### Modified Capabilities

## Impact

- New test project files under `DndMcpAICsharpFun.Tests/`
- No production code changes
- Adds `Microsoft.AspNetCore.Mvc.Testing` package to test project (for `WebApplicationFactory`)
- Adds `Microsoft.Data.Sqlite` package to test project (for in-memory SQLite connection management)
