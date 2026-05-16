## Context

`DndMcpAICompanion/Program.cs` is ~100 lines of flat top-level statements. There is no structural separation between configuration loading, service registration, database seeding, middleware, and endpoint mapping. The main API already solved this with the `program-structure` pattern; this design applies the same approach to the companion app.

The one hard constraint: MCP client initialisation is async (`await McpClient.CreateAsync`, `await mcpClient.ListToolsAsync`). Standard `IServiceCollection` extension methods are synchronous and cannot await, so those two calls must stay in Program.cs and their results passed into the extension.

## Goals / Non-Goals

**Goals:**

- `Program.cs` reduced to ~25 lines: configuration loading, option binding, async MCP init, extension method calls, `app.Run()`
- One file per concern in `DndMcpAICompanion/Extensions/`
- Each extension method is independently readable and testable in isolation
- Zero behaviour changes

**Non-Goals:**

- Changing any runtime behaviour (auth flow, rate limit settings, MCP tool registration, etc.)
- Introducing new abstractions or interfaces beyond the extension methods themselves
- Making MCP init injectable / async-friendly (out of scope for this change)

## Decisions

### One file per concern, not one mega-file

The existing `program-structure` spec names `ServiceCollectionExtensions.cs` (one file). For the companion we use one file per concern (8 files) because the companion has fewer services total and fine-grained files are easier to navigate. A single 200-line extensions file would just move the noise.

**Alternatives considered:** Single file — rejected because it recreates the same readability problem.

### Extension methods on `IServiceCollection` / `WebApplicationBuilder` / `WebApplication`

- Configuration loading → `WebApplicationBuilder` extension (`AddDndConfiguration`)
- Service registrations → `IServiceCollection` extensions (standard pattern)
- MCP registration → `IServiceCollection` extension that accepts pre-built `McpClient` + tools list
- Post-build startup (DB init, seed) → `WebApplication` extension returning `Task` (`InitializeDatabaseAsync`)
- Middleware → `WebApplication` extension (`UseDndMiddleware`)
- Endpoints → `WebApplication` extension (`MapDndEndpoints`)

**Alternatives considered:** Moving DB init into a hosted service — unnecessary complexity for a single-instance app with a startup-time DB.

### Async MCP stays in Program.cs

MCP client creation requires two awaited calls before services can be registered. Wrapping this in a factory method adds indirection without eliminating the `await`. Keeping the two lines in Program.cs is the most honest approach.

## Risks / Trade-offs

- **Risk**: Extension files are thin wrappers with no logic → they become boilerplate if services grow significantly.  

  **Mitigation**: Each extension grows with its concern; splitting further is easy later.

- **Risk**: Passing `McpClient` + tools into `AddMcpClient` as parameters instead of resolving from config couples the call site.  

  **Mitigation**: Accepted trade-off — async init is a genuine seam that can't be hidden in sync registration.

## Migration Plan

Pure refactor — no configuration changes, no environment-specific steps, no rollback concern beyond reverting the commit.
