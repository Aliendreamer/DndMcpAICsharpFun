# companion-program-structure Specification

## Purpose
TBD - created by archiving change program-cs-extensions. Update Purpose after archive.
## Requirements
### Requirement: Companion Program.cs delegates configuration loading to an extension method
The system SHALL provide an extension method `AddDndConfiguration` on `WebApplicationBuilder` in `Extensions/ConfigurationExtensions.cs` that loads `Config/appsettings.json`, the environment-specific overlay, and environment variables, so `Program.cs` makes a single call instead of three chained `AddJsonFile`/`AddEnvironmentVariables` calls.

#### Scenario: Configuration sources are loaded via extension method

- **WHEN** `builder.AddDndConfiguration()` is called
- **THEN** `Config/appsettings.json`, `Config/appsettings.<env>.json`, and environment variables are added to the configuration pipeline

### Requirement: Companion Program.cs delegates database service registration to an extension method
The system SHALL provide an extension method `AddDatabase` on `IServiceCollection` in `Extensions/DatabaseExtensions.cs` that resolves the SQLite connection string, ensures the data directory exists, and registers `UserRepository`, `CampaignRepository`, `HeroRepository`, and `ChatRateLimiter` as singletons.

#### Scenario: Database services are registered via extension method

- **WHEN** `services.AddDatabase(configuration)` is called
- **THEN** `UserRepository`, `CampaignRepository`, `HeroRepository`, and `ChatRateLimiter` are registered in the DI container with a shared connection string derived from `Data:CompanionDb`

### Requirement: Companion Program.cs delegates Ollama chat service registration to an extension method
The system SHALL provide an extension method `AddDndChat` on `IServiceCollection` in `Extensions/ChatExtensions.cs` that registers `IHttpContextAccessor`, a transient `IChatClient` (Ollama with function invocation), and scoped `DndChatService`.

#### Scenario: Chat services are registered via extension method

- **WHEN** `services.AddDndChat(ollamaOpts)` is called
- **THEN** `IHttpContextAccessor`, `IChatClient` (OllamaChatClient with `UseFunctionInvocation`), and `DndChatService` are registered in the DI container

### Requirement: Companion Program.cs delegates MCP client registration to an extension method
The system SHALL provide an extension method `AddMcpClient` on `IServiceCollection` in `Extensions/McpExtensions.cs` that accepts an already-initialised `McpClient` and tools list and registers them as singletons, allowing `Program.cs` to retain the async initialisation while keeping registration logic out of the entry point.

#### Scenario: MCP client is registered via extension method

- **WHEN** `services.AddMcpClient(mcpClient, mcpTools)` is called with a pre-built client and tools list
- **THEN** `McpClient` and `IReadOnlyList<AITool>` are registered as singletons in the DI container

### Requirement: Companion Program.cs delegates authentication registration to an extension method
The system SHALL provide an extension method `AddDndAuthentication` on `IServiceCollection` in `Extensions/AuthExtensions.cs` that registers cookie authentication with `/login` and `/logout` paths, and adds authorization services.

#### Scenario: Authentication is registered via extension method

- **WHEN** `services.AddDndAuthentication()` is called
- **THEN** cookie authentication with `LoginPath = "/login"` and `LogoutPath = "/logout"` and authorization services are registered in the DI container

### Requirement: Companion Program.cs delegates rate limiting registration to an extension method
The system SHALL provide an extension method `AddDndRateLimiting` on `IServiceCollection` in `Extensions/RateLimitExtensions.cs` that registers a sliding window rate limiter named `"global"` using settings from configuration.

#### Scenario: Rate limiting is registered via extension method

- **WHEN** `services.AddDndRateLimiting(configuration)` is called
- **THEN** a sliding window limiter named `"global"` is registered with permit limit and window values from `RateLimit:RequestsPerMinute`

### Requirement: Companion Program.cs delegates Blazor/Razor registration to an extension method
The system SHALL provide an extension method `AddDndBlazor` on `IServiceCollection` in `Extensions/BlazorExtensions.cs` that calls `AddRazorComponents().AddInteractiveServerComponents()`.

#### Scenario: Blazor is registered via extension method

- **WHEN** `services.AddDndBlazor()` is called
- **THEN** Razor components with interactive server render mode are registered in the DI container

### Requirement: Companion Program.cs delegates database initialisation to an extension method
The system SHALL provide an extension method `InitializeDatabaseAsync` on `WebApplication` in `Extensions/AppExtensions.cs` that calls `InitializeAsync` on all repositories and seeds the test user if absent.

#### Scenario: Database is initialised via extension method

- **WHEN** `await app.InitializeDatabaseAsync()` is called on the built application
- **THEN** `UserRepository.InitializeAsync`, `CampaignRepository.InitializeAsync` are awaited and the `"test"` user is created if it does not exist

### Requirement: Companion Program.cs delegates middleware pipeline setup to an extension method
The system SHALL provide an extension method `UseDndMiddleware` on `WebApplication` in `Extensions/AppExtensions.cs` that calls `UseStaticFiles`, `UseRateLimiter`, `UseAuthentication`, `UseAuthorization`, and `UseAntiforgery` in the correct order.

#### Scenario: Middleware pipeline is configured via extension method

- **WHEN** `app.UseDndMiddleware()` is called
- **THEN** static files, rate limiter, authentication, authorization, and antiforgery middleware are added to the pipeline in that order

### Requirement: Companion Program.cs delegates endpoint mapping to an extension method
The system SHALL provide an extension method `MapDndEndpoints` on `WebApplication` in `Extensions/AppExtensions.cs` that maps the `/logout` GET endpoint and the Razor component tree with interactive server render mode and global rate limiting.

#### Scenario: Endpoints are mapped via extension method

- **WHEN** `app.MapDndEndpoints(mcpOpts)` is called
- **THEN** the `/logout` route and Razor components (`App`) with `RequireRateLimiting("global")` are registered

