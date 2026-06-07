## 1. Create Extensions folder and per-concern files

- [x] 1.1 Create `DndMcpAICompanion/Extensions/ConfigurationExtensions.cs` with `AddDndConfiguration(this WebApplicationBuilder builder)` — moves the three `AddJsonFile` / `AddEnvironmentVariables` calls out of Program.cs
- [x] 1.2 Create `DndMcpAICompanion/Extensions/DatabaseExtensions.cs` with `AddDatabase(this IServiceCollection services, IConfiguration config)` — DB path resolution, `Directory.CreateDirectory`, and singleton registrations for `UserRepository`, `CampaignRepository`, `HeroRepository`, `ChatRateLimiter`
- [x] 1.3 Create `DndMcpAICompanion/Extensions/ChatExtensions.cs` with `AddDndChat(this IServiceCollection services, OllamaOptions ollamaOpts)` — registers `IHttpContextAccessor`, transient `IChatClient` (Ollama + function invocation), scoped `DndChatService`
- [x] 1.4 Create `DndMcpAICompanion/Extensions/McpExtensions.cs` with `AddMcpClient(this IServiceCollection services, McpClient mcpClient, IReadOnlyList<AITool> mcpTools)` — registers pre-built client and tools list as singletons
- [x] 1.5 Create `DndMcpAICompanion/Extensions/AuthExtensions.cs` with `AddDndAuthentication(this IServiceCollection services)` — cookie auth with `/login`/`/logout` paths plus authorization services
- [x] 1.6 Create `DndMcpAICompanion/Extensions/RateLimitExtensions.cs` with `AddDndRateLimiting(this IServiceCollection services, IConfiguration config)` — sliding window limiter named `"global"` with values from `RateLimit:RequestsPerMinute`
- [x] 1.7 Create `DndMcpAICompanion/Extensions/BlazorExtensions.cs` with `AddDndBlazor(this IServiceCollection services)` — calls `AddRazorComponents().AddInteractiveServerComponents()`
- [x] 1.8 Create `DndMcpAICompanion/Extensions/AppExtensions.cs` with three methods:
  - `InitializeDatabaseAsync(this WebApplication app)` — awaits `UserRepository.InitializeAsync`, `CampaignRepository.InitializeAsync`, seeds test user
  - `UseDndMiddleware(this WebApplication app)` — `UseStaticFiles`, `UseRateLimiter`, `UseAuthentication`, `UseAuthorization`, `UseAntiforgery` in order
  - `MapDndEndpoints(this WebApplication app, AppMcpClientOptions mcpOpts)` — maps `/logout` GET and Razor `App` component tree with `RequireRateLimiting("global")`; also registers the `ApplicationStopping` disposal of the MCP client and the missing-key warning log

## 2. Rewrite Program.cs to thin composition root

- [x] 2.1 Replace `Program.cs` body with: `builder.AddDndConfiguration()`, option binding for `OllamaOptions` and `AppMcpClientOptions`, async MCP init (transport → `McpClient.CreateAsync` → `ListToolsAsync`), then `builder.Services.Add*` calls using the new extension methods, `var app = builder.Build()`, `await app.InitializeDatabaseAsync()`, `app.UseDndMiddleware()`, `app.MapDndEndpoints(mcpOpts)`, `app.Run()`

## 3. Verify

- [x] 3.1 Run `dotnet build DndMcpAICompanion` — zero errors, zero warnings
- [x] 3.2 Run the companion app locally and confirm login, chat, and campaign pages load correctly
