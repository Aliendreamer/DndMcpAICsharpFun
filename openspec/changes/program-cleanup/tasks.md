## 1. ServiceCollectionExtensions.cs

- [ ] 1.1 Create `Extensions/ServiceCollectionExtensions.cs` with `AddInfrastructureClients(this IServiceCollection services, IConfiguration configuration)` — move QdrantClient, OllamaApiClient, and IngestionDbContext registrations from Program.cs
- [ ] 1.2 Add `AddIngestionPipeline(this IServiceCollection services)` to the same file — move IIngestionTracker, IPdfTextExtractor, all IPatternDetector registrations, ContentCategoryDetector, DndChunker, IEmbeddingIngestor, IEmbeddingService, IVectorStoreService, IEmbeddingIngestor, QdrantCollectionInitializer, and IngestionBackgroundService
- [ ] 1.3 Add `AddRetrieval(this IServiceCollection services)` — move IQdrantSearchClient adapter, IRagRetrievalService
- [ ] 1.4 Add `AddObservability(this IServiceCollection services, IConfiguration configuration)` — move OTel registration block, read OpenTelemetryOptions internally
- [ ] 1.5 Run `dotnet build` — 0 errors, 0 warnings

## 2. WebApplicationExtensions.cs

- [ ] 2.1 Create `Extensions/WebApplicationExtensions.cs` with `MigrateDatabaseAsync(this WebApplication app)` — move the EF MigrateAsync scope block
- [ ] 2.2 Add `ValidateStartupConfiguration(this WebApplication app)` — move the admin key guard
- [ ] 2.3 Add `MapAdminMiddleware(this WebApplication app)` — move the UseWhen /admin block
- [ ] 2.4 Add `MapObservabilityEndpoints(this WebApplication app)` — move the conditional MapPrometheusScrapingEndpoint call, read OpenTelemetryOptions from app.Configuration
- [ ] 2.5 Run `dotnet build` — 0 errors, 0 warnings

## 3. Refactor Program.cs

- [ ] 3.1 Replace all extracted registrations in Program.cs with calls to the extension methods; keep Configure<Xxx> options bindings and health checks inline
- [ ] 3.2 Replace inline startup code with `await app.MigrateDatabaseAsync()`, `app.ValidateStartupConfiguration()`, `app.MapAdminMiddleware()`, `app.MapObservabilityEndpoints()`
- [ ] 3.3 Remove now-unused using directives from Program.cs
- [ ] 3.4 Run `dotnet build` — 0 errors, 0 warnings

## 4. Verify and Commit

- [ ] 4.1 Run `dotnet test` — all tests pass
- [ ] 4.2 Commit: `refactor: extract Program.cs registrations into ServiceCollection and WebApplication extension methods`
