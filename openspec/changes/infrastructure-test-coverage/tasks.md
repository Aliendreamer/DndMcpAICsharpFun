## 1. Phase 1 — SqliteIngestionTracker Tests

- [ ] 1.1 Add `Microsoft.Data.Sqlite` package to `DndMcpAICsharpFun.Tests` (needed to keep a `SqliteConnection` open for `:memory:` DB lifetime)
- [ ] 1.2 Create `DndMcpAICsharpFun.Tests/Infrastructure/Sqlite/TrackerFixture.cs` — opens a `SqliteConnection("DataSource=:memory:")`, runs `MigrateAsync()`, exposes a factory for fresh `IngestionDbContext` instances over that connection, implements `IDisposable` to close the connection
- [ ] 1.3 Create `DndMcpAICsharpFun.Tests/Infrastructure/Sqlite/SqliteIngestionTrackerTests.cs` with a test class that uses `TrackerFixture` via constructor injection (implements `IDisposable`)
- [ ] 1.4 Write test: `CreateAsync_AssignsId_AndReturnsRecord`
- [ ] 1.5 Write test: `GetByIdAsync_ExistingId_ReturnsRecord`
- [ ] 1.6 Write test: `GetByIdAsync_MissingId_ReturnsNull`
- [ ] 1.7 Write test: `GetAllAsync_ReturnsAllRecords`
- [ ] 1.8 Write test: `MarkHashAsync_UpdatesFileHash`
- [ ] 1.9 Write test: `MarkExtractedAsync_SetsStatusExtracted`
- [ ] 1.10 Write test: `MarkJsonIngestedAsync_SetsStatusAndChunkCount`
- [ ] 1.11 Write test: `MarkFailedAsync_SetsStatusAndReason`
- [ ] 1.12 Write test: `ResetForReingestionAsync_ResetsStatusToPending`
- [ ] 1.13 Write test: `DeleteAsync_RemovesRecord`
- [ ] 1.14 Run `dotnet test --filter "SqliteIngestionTrackerTests"` — all tests must pass

## 2. Phase 2 — OllamaLlmClassifier Tests

- [ ] 2.1 Create `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaLlmClassifierTests.cs`
- [ ] 2.2 Write test: `ClassifyPageAsync_ValidJsonArray_ReturnsCategories` — mock `IOllamaApiClient` returns `["Spell","Monster"]`, assert result contains both
- [ ] 2.3 Write test: `ClassifyPageAsync_EmptyArray_ReturnsEmptyList` — mock returns `[]`
- [ ] 2.4 Write test: `ClassifyPageAsync_InvalidJson_ReturnsEmptyList` — mock returns garbage text
- [ ] 2.5 Write test: `ClassifyPageAsync_EmptyString_ReturnsEmptyList` — mock returns empty string
- [ ] 2.6 Write test: `ClassifyPageAsync_CancelledToken_ThrowsOperationCancelled`
- [ ] 2.7 Run `dotnet test --filter "OllamaLlmClassifierTests"` — all tests must pass

## 3. Phase 3 — BooksAdminEndpoints Tests

- [ ] 3.1 Add `Microsoft.AspNetCore.Mvc.Testing` package to `DndMcpAICsharpFun.Tests`
- [ ] 3.2 Create `DndMcpAICsharpFun.Tests/Admin/BooksAdminFactory.cs` — `WebApplicationFactory<Program>` that replaces `AdminApiKeyMiddleware` with a passthrough and registers NSubstitute mocks for `IIngestionTracker`, `IIngestionQueue`, `IIngestionOrchestrator`, `IEntityJsonStore`, `IExtractionCancellationRegistry` via `ConfigureTestServices`; exposes mock accessors
- [ ] 3.3 Add `<InternalsVisibleTo Include="DndMcpAICsharpFun.Tests" />` to the main project if not already present (needed for `Program` visibility to `WebApplicationFactory`)
- [ ] 3.4 Add `public partial class Program { }` stub at the bottom of `Program.cs` if not already present
- [ ] 3.5 Create `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs` using `IClassFixture<BooksAdminFactory>`
- [ ] 3.6 Write tests for `POST /admin/books/register`: valid PDF → 202, non-PDF → 400, invalid version → 400
- [ ] 3.7 Write tests for `POST /admin/books/register-path`: valid path → 202, file not found → 400, invalid version → 400
- [ ] 3.8 Write test: `GET /admin/books` → 200 with list
- [ ] 3.9 Write tests for `POST /admin/books/{id}/extract`: not found → 404, conflict → 409, success → 202
- [ ] 3.10 Write tests for `GET /admin/books/{id}/extracted`: not found → 404, success → 200
- [ ] 3.11 Write tests for `POST /admin/books/{id}/ingest-json`: not found → 404, conflict → 409, success → 202
- [ ] 3.12 Write tests for `DELETE /admin/books/{id}`: not found → 404, conflict → 409, success → 204
- [ ] 3.13 Write tests for `POST /admin/books/{id}/cancel-extract`: not found → 404, success → 200
- [ ] 3.14 Run `dotnet test --filter "BooksAdminEndpointsTests"` — all tests must pass
