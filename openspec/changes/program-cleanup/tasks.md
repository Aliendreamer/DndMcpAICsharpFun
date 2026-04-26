## 1. ServiceCollectionExtensions.cs

- [x] 1.1 Create `Extensions/ServiceCollectionExtensions.cs` with `AddInfrastructureClients(this IServiceCollection services, IConfiguration configuration)` — move QdrantClient, OllamaApiClient, and IngestionDbContext registrations from Program.cs
- [x] 1.2 Add `AddIngestionPipeline(this IServiceCollection services)` to the same file — move IIngestionTracker, IPdfTextExtractor, all IPatternDetector registrations, ContentCategoryDetector, DndChunker, IEmbeddingIngestor, IEmbeddingService, IVectorStoreService, IEmbeddingIngestor, QdrantCollectionInitializer, and IngestionBackgroundService
- [x] 1.3 Add `AddRetrieval(this IServiceCollection services)` — move IQdrantSearchClient adapter, IRagRetrievalService
- [x] 1.4 Add `AddObservability(this IServiceCollection services, IConfiguration configuration)` — move OTel registration block, read OpenTelemetryOptions internally
- [x] 1.5 Run `dotnet build` — 0 errors, 0 warnings

## 2. WebApplicationExtensions.cs

- [x] 2.1 Create `Extensions/WebApplicationExtensions.cs` with `MigrateDatabaseAsync(this WebApplication app)` — move the EF MigrateAsync scope block
- [x] 2.2 Add `ValidateStartupConfiguration(this WebApplication app)` — move the admin key guard
- [x] 2.3 Add `MapAdminMiddleware(this WebApplication app)` — move the UseWhen /admin block
- [x] 2.4 Add `MapObservabilityEndpoints(this WebApplication app)` — move the conditional MapPrometheusScrapingEndpoint call, read OpenTelemetryOptions from app.Configuration
- [x] 2.5 Run `dotnet build` — 0 errors, 0 warnings

## 3. Refactor Program.cs

- [x] 3.1 Replace all extracted registrations in Program.cs with calls to the extension methods; keep Configure<Xxx> options bindings and health checks inline
- [x] 3.2 Replace inline startup code with `await app.MigrateDatabaseAsync()`, `app.ValidateStartupConfiguration()`, `app.MapAdminMiddleware()`, `app.MapObservabilityEndpoints()`
- [x] 3.3 Remove now-unused using directives from Program.cs
- [x] 3.4 Run `dotnet build` — 0 errors, 0 warnings

## 4. Verify and Commit

- [x] 4.1 Run `dotnet test` — all tests pass
- [x] 4.2 Commit: `refactor: extract Program.cs registrations into ServiceCollection and WebApplication extension methods`
