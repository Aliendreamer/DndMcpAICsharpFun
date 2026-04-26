## Context

`Program.cs` is 148 lines of flat top-level statements covering configuration binding, infrastructure client construction, DI registrations for five feature areas, hosted services, health checks, OTel setup, EF migrations, startup validation, middleware, and endpoint mapping. The file has no internal structure beyond comments. As the app grows (MCP server, UI, etc.) this will become unmanageable.

## Goals / Non-Goals

**Goals:**
- Extract DI registrations into `Extensions/ServiceCollectionExtensions.cs` with four focused methods
- Extract app-level concerns into `Extensions/WebApplicationExtensions.cs` with four focused methods
- Reduce `Program.cs` to a readable ~30-line composition root
- Zero behavior changes — all tests pass, all endpoints identical

**Non-Goals:**
- No changes to any Feature or Infrastructure files
- No changes to configuration, options classes, or appsettings
- No introduction of new abstractions or interfaces
- No splitting into multiple Program.cs-style partial classes

## Decisions

**Two files split by extension target (IServiceCollection vs WebApplication)**
Alternatives: one file per feature area (6 files), single file for everything (1 file). The two-file split keeps the number of files minimal while drawing the clearest architectural line: services vs app pipeline. The feature-per-file approach is better for larger codebases but adds navigation overhead now.

**Options and health checks stay inline in Program.cs**
`Configure<Xxx>()` calls are 5 lines and read as a concise options manifest — extracting them adds a method call with no clarity benefit. Health checks are similarly short and change infrequently. Both stay inline.

**`AddObservability` reads IConfiguration internally**
Rather than accept `OpenTelemetryOptions` as a parameter, the method calls `configuration.GetSection("OpenTelemetry").Get<OpenTelemetryOptions>()` itself. This keeps the call site clean (`builder.Services.AddObservability(builder.Configuration)`) and is consistent with how `AddInfrastructureClients` handles its config.

**`MapObservabilityEndpoints` re-reads IConfiguration from app.Configuration**
The `otelOptions` variable currently spans builder setup and app setup. After extraction, each method is self-contained and reads what it needs from `app.Configuration`. No shared state needed.

**`MigrateDatabaseAsync` and `ValidateStartupConfiguration` on WebApplication**
These are startup-time app concerns, not service registrations, so they belong on `WebApplication` alongside middleware and endpoint mapping.

## Risks / Trade-offs

[Risk] Extension methods make call order less explicit than inline code → Mitigation: XML doc comments on each method document any ordering constraints (e.g. `QdrantCollectionInitializer` must be registered before `IngestionBackgroundService` — enforced by order within `AddIngestionPipeline`).

[Risk] Build fails if usings are incomplete in new files → Mitigation: `dotnet build` is the first verification step; `TreatWarningsAsErrors=true` catches anything missed.
