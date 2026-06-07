## Why

`DndMcpAICompanion/Program.cs` is a ~100-line flat file mixing configuration loading, option binding, async MCP initialisation, six distinct service groups, database seeding, middleware wiring, and endpoint mapping — the same structural noise the main API already fixed with extension methods. Bringing the companion app into line with the existing `program-structure` spec makes the entry point readable and each concern independently navigable.

## What Changes

- Add `DndMcpAICompanion/Extensions/` folder containing one file per concern
- Extract service registrations into `IServiceCollection` or `WebApplicationBuilder` extension methods
- Extract post-build startup into `WebApplication` extension methods
- Reduce `Program.cs` to: configuration loading, option binding, async MCP init (must stay due to `await`), calls to the extension methods, and `app.Run()`

## Capabilities

### New Capabilities

- `companion-program-structure`: Extension-method composition root for the companion Blazor app — mirrors the spirit of the existing `program-structure` spec but scoped to companion services (Ollama chat, MCP client, SQLite repositories, cookie auth, rate limiting, Razor/Blazor).

### Modified Capabilities

_(none — the companion app has no existing spec-level requirements being changed)_

## Impact

- `DndMcpAICompanion/Program.cs` — rewritten to thin composition root
- New files: `Extensions/ConfigurationExtensions.cs`, `DatabaseExtensions.cs`, `ChatExtensions.cs`, `McpExtensions.cs`, `AuthExtensions.cs`, `RateLimitExtensions.cs`, `BlazorExtensions.cs`, `AppExtensions.cs`
- No behaviour changes, no new dependencies
