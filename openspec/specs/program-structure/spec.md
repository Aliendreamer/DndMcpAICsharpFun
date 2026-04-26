# Spec: program-structure

## Purpose

Define how `Program.cs` is structured as a thin composition root that delegates all service registration and app pipeline setup to named extension methods, keeping the entry point readable and focused.

## Requirements

### Requirement: Program.cs delegates service registration to IServiceCollection extension methods
The system SHALL provide extension methods on `IServiceCollection` in `Extensions/ServiceCollectionExtensions.cs` that encapsulate all DI registrations, allowing `Program.cs` to call named methods rather than contain inline registration logic.

#### Scenario: Infrastructure clients are registered via extension method
- **WHEN** `AddInfrastructureClients` is called with the application configuration
- **THEN** `QdrantClient`, `OllamaApiClient`, and `IngestionDbContext` are registered in the DI container

#### Scenario: Ingestion pipeline is registered via extension method
- **WHEN** `AddIngestionPipeline` is called
- **THEN** `IIngestionTracker`, `IPdfTextExtractor`, all `IPatternDetector` implementations, `ContentCategoryDetector`, `DndChunker`, `IEmbeddingIngestor`, and both hosted services are registered in the DI container

#### Scenario: Retrieval services are registered via extension method
- **WHEN** `AddRetrieval` is called
- **THEN** `IEmbeddingService`, `IVectorStoreService`, `IQdrantSearchClient`, and `IRagRetrievalService` are registered in the DI container

#### Scenario: Observability is registered via extension method
- **WHEN** `AddObservability` is called with the application configuration and `OpenTelemetry:Enabled` is `true`
- **THEN** OpenTelemetry with ASP.NET Core, HttpClient, and runtime instrumentation is registered

#### Scenario: Observability is skipped when disabled
- **WHEN** `AddObservability` is called and `OpenTelemetry:Enabled` is `false`
- **THEN** no OpenTelemetry services are registered

### Requirement: Program.cs delegates app pipeline setup to WebApplication extension methods
The system SHALL provide extension methods on `WebApplication` in `Extensions/WebApplicationExtensions.cs` that encapsulate startup operations and pipeline configuration.

#### Scenario: Database is migrated via extension method
- **WHEN** `MigrateDatabaseAsync` is called on the built application
- **THEN** pending EF Core migrations are applied to the SQLite database

#### Scenario: Startup configuration is validated via extension method
- **WHEN** `ValidateStartupConfiguration` is called and the admin API key is blank
- **THEN** an `InvalidOperationException` is thrown preventing the app from starting

#### Scenario: Admin middleware is mapped via extension method
- **WHEN** `MapAdminMiddleware` is called
- **THEN** `AdminApiKeyMiddleware` is applied to all requests matching `/admin/*`

#### Scenario: Observability endpoint is mapped via extension method
- **WHEN** `MapObservabilityEndpoints` is called and `OpenTelemetry:Enabled` is `true`
- **THEN** the `/metrics` Prometheus scraping endpoint is registered

### Requirement: Program.cs is reduced to a composition root
The system SHALL ensure `Program.cs` contains only configuration binding, options registration, health check registration, calls to the extension methods above, and `app.Run()` — with no inline DI registration logic or startup imperative code beyond extension method calls.

#### Scenario: Program.cs has no inline service factory lambdas
- **WHEN** `Program.cs` is read
- **THEN** there are no `sp =>` lambda expressions for constructing infrastructure clients inline
