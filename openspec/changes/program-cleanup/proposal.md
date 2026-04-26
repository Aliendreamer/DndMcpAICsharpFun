## Why

`Program.cs` has grown to 148 lines of flat service registrations and middleware configuration with no logical separation, making it hard to navigate and increasingly difficult to extend. Extracting the registrations into focused extension methods gives each feature area a clear home and reduces `Program.cs` to a readable summary of the application's composition.

## What Changes

- Create `Extensions/ServiceCollectionExtensions.cs` with four `IServiceCollection` extension methods: `AddInfrastructureClients`, `AddIngestionPipeline`, `AddRetrieval`, `AddObservability`
- Create `Extensions/WebApplicationExtensions.cs` with four `WebApplication` extension methods: `MigrateDatabaseAsync`, `ValidateStartupConfiguration`, `MapAdminMiddleware`, `MapObservabilityEndpoints`
- Refactor `Program.cs` to call these extension methods — options (`Configure<Xxx>`) and health checks remain inline
- No behavior changes; all existing tests must continue to pass

## Capabilities

### New Capabilities

- `program-structure`: `Program.cs` is organized via extension methods in an `Extensions/` folder — two files split by `IServiceCollection` vs `WebApplication` concerns

### Modified Capabilities

None — this is a pure structural refactor. No requirement-level behavior changes.

## Impact

- `Program.cs` — refactored (reduced to ~30 lines)
- `Extensions/ServiceCollectionExtensions.cs` — new file
- `Extensions/WebApplicationExtensions.cs` — new file
- All other source files unchanged
- No API surface changes, no configuration changes, no dependency changes
