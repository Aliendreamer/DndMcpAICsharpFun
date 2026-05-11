## 1. Project Scaffold

- [ ] 1.1 Create `DndMcpAICompanion/DndMcpAICompanion.csproj` targeting `net10.0` with Blazor Server, `Microsoft.Extensions.AI.Ollama`, and `ModelContextProtocol.AspNetCore` packages
- [ ] 1.2 Add the new project to `DndMcpAICsharpFun.slnx`
- [ ] 1.3 Create `DndMcpAICompanion/Config/appsettings.json` with `Ollama` and `Mcp` sections (empty `ApiKey`)
- [ ] 1.4 Create `DndMcpAICompanion/Config/appsettings.Development.json` with localhost URLs and `devMcpKey`

## 2. Configuration Options

- [ ] 2.1 Create `Features/Chat/OllamaOptions.cs` — record with `Url` and `Model` properties bound to `"Ollama"` section
- [ ] 2.2 Create `Features/Chat/McpClientOptions.cs` — record with `Url` and `ApiKey` properties bound to `"Mcp"` section

## 3. MCP Client Integration

- [ ] 3.1 Register `OllamaChatClient` as `IChatClient` singleton in `Program.cs` using `OllamaOptions`
- [ ] 3.2 Register `McpClientFactory` / MCP client singleton in `Program.cs` pointing at `McpClientOptions.Url` with `X-Mcp-Api-Key` header from `McpClientOptions.ApiKey`
- [ ] 3.3 Verify MCP tools (`search_lore`, `search_entities`, `get_entity`) are discoverable from the client at startup

## 4. DndChatService

- [ ] 4.1 Create `Features/Chat/DndChatService.cs` — scoped service holding `List<ChatMessage> History` and injecting `IChatClient` and the MCP tool functions
- [ ] 4.2 Implement `Task<string> SendAsync(string userMessage, CancellationToken ct)` — appends user message, calls `IChatClient.CompleteAsync` with MCP tools, appends reply, returns reply text
- [ ] 4.3 On exception (Ollama unreachable), catch and return `"The AI is unavailable right now. Please try again."` without rethrowing

## 5. Chat UI

- [ ] 5.1 Create `Components/Pages/Chat.razor` at route `/` — scrollable message list (user messages right-aligned, assistant left-aligned) with text input and Send button at the bottom
- [ ] 5.2 Wire Send button and Enter key to call `DndChatService.SendAsync`, disable input and show loading indicator while waiting
- [ ] 5.3 Show initial greeting message from the assistant on first load
- [ ] 5.4 Auto-scroll the message list to the bottom after each new message

## 6. Program.cs and Blazor Setup

- [ ] 6.1 Create `Program.cs` — configure `WebApplication` with Blazor Server interactive components, register options (`OllamaOptions`, `McpClientOptions`), register services (`DndChatService` scoped), map Blazor hub
- [ ] 6.2 Create `Components/App.razor`, `Components/Routes.razor`, and `Components/Layout/MainLayout.razor` with minimal Blazor Server boilerplate

## 7. Docker Compose

- [ ] 7.1 Create `DndMcpAICompanion/Dockerfile` — multi-stage build (`dotnet publish` → runtime image), expose port 8080
- [ ] 7.2 Add `companion` service to `docker-compose.yml` on port `5102:8080`, depends on `app` and `ollama`, same `dnd_net` network, `Mcp__ApiKey` environment variable

## 8. Tests

- [ ] 8.1 Add a test project reference or test class in `DndMcpAICsharpFun.Tests` (or a new `DndMcpAICompanion.Tests` project) with one smoke test: instantiate `DndChatService` with a fake `IChatClient` returning a fixed string, call `SendAsync`, assert the reply is appended to `History`
- [ ] 8.2 Run `dotnet build` and `dotnet test` — all tests pass
