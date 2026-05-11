# D&D Companion — Blazor Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Blazor Server chat app (`DndMcpAICompanion`) that lets users ask D&D questions; Ollama reasons over the answer using the MCP server's tools (`search_lore`, `search_entities`, `get_entity`).

**Architecture:** New project in the existing solution, same repo, separate Docker Compose service on port 5102. `Microsoft.Extensions.AI` wraps Ollama as `IChatClient`; `ModelContextProtocol` client SDK connects to `/mcp` and surfaces the server's tools as `AIFunction` objects passed to every `CompleteAsync` call. All state (conversation history) lives in a Blazor-scoped service — one instance per browser tab, reset on refresh.

**Tech Stack:** .NET 10, Blazor Server (Interactive Server render mode), `Microsoft.Extensions.AI.Ollama` 9.7.0-preview, `ModelContextProtocol` 1.3.0 (client side), xUnit, FluentAssertions.

---

## File Map

```
DndMcpAICompanion/
  DndMcpAICompanion.csproj
  Program.cs
  Config/
    appsettings.json
    appsettings.Development.json
  Features/Chat/
    OllamaOptions.cs
    McpClientOptions.cs
    DndChatService.cs
  Components/
    App.razor
    Routes.razor
    Layout/
      MainLayout.razor
    Pages/
      Chat.razor
  wwwroot/
    app.css
    app.js
  Dockerfile

DndMcpAICompanion.Tests/
  DndMcpAICompanion.Tests.csproj
  Chat/
    DndChatServiceTests.cs

(modified)
DndMcpAICsharpFun.slnx          ← add two new projects
docker-compose.yml               ← add companion service
```

---

### Task 1: Project Scaffold

**Files:**
- Create: `DndMcpAICompanion/DndMcpAICompanion.csproj`
- Create: `DndMcpAICompanion/Config/appsettings.json`
- Create: `DndMcpAICompanion/Config/appsettings.Development.json`
- Modify: `DndMcpAICsharpFun.slnx`

- [ ] **Step 1: Create the project file**

Create `DndMcpAICompanion/DndMcpAICompanion.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.AI.Ollama" Version="9.7.0-preview.1.25356.2" />
    <PackageReference Include="ModelContextProtocol" Version="1.3.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create appsettings.json**

Create `DndMcpAICompanion/Config/appsettings.json`:

```json
{
  "Ollama": {
    "Url": "http://ollama:11434",
    "Model": "qwen3:8b"
  },
  "Mcp": {
    "Url": "http://app:5101/mcp",
    "ApiKey": ""
  }
}
```

- [ ] **Step 3: Create appsettings.Development.json**

Create `DndMcpAICompanion/Config/appsettings.Development.json`:

```json
{
  "Ollama": {
    "Url": "http://localhost:11434",
    "Model": "qwen3:8b"
  },
  "Mcp": {
    "Url": "http://localhost:5101/mcp",
    "ApiKey": "devMcpKey"
  }
}
```

- [ ] **Step 4: Add projects to solution**

Edit `DndMcpAICsharpFun.slnx` to add both the app and its test project:

```xml
<Solution>
  <Folder Name="/Tools/">
    <Project Path="Tools/SchemaGenerator/SchemaGenerator.csproj" />
  </Folder>
  <Project Path="DndMcpAICsharpFun.csproj" />
  <Project Path="DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj" />
  <Project Path="DndMcpAICompanion/DndMcpAICompanion.csproj" />
  <Project Path="DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj" />
</Solution>
```

- [ ] **Step 5: Verify the project restores**

```bash
dotnet restore DndMcpAICompanion/DndMcpAICompanion.csproj
```

Expected: `Restore completed` with no errors.

---

### Task 2: Configuration Options

**Files:**
- Create: `DndMcpAICompanion/Features/Chat/OllamaOptions.cs`
- Create: `DndMcpAICompanion/Features/Chat/McpClientOptions.cs`

- [ ] **Step 1: Create OllamaOptions**

Create `DndMcpAICompanion/Features/Chat/OllamaOptions.cs`:

```csharp
namespace DndMcpAICompanion.Features.Chat;

public sealed class OllamaOptions
{
    public string Url { get; init; } = string.Empty;
    public string Model { get; init; } = "qwen3:8b";
}
```

- [ ] **Step 2: Create McpClientOptions**

Create `DndMcpAICompanion/Features/Chat/McpClientOptions.cs`:

```csharp
namespace DndMcpAICompanion.Features.Chat;

public sealed class McpClientOptions
{
    public string Url { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
}
```

- [ ] **Step 3: Build to confirm no errors**

```bash
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj -q
```

Expected: `Build succeeded. 0 Error(s).`

---

### Task 3: DndChatService (TDD)

**Files:**
- Create: `DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj`
- Create: `DndMcpAICompanion.Tests/Chat/DndChatServiceTests.cs`
- Create: `DndMcpAICompanion/Features/Chat/DndChatService.cs`

- [ ] **Step 1: Create the test project**

Create `DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="6.12.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DndMcpAICompanion\DndMcpAICompanion.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the failing tests**

Create `DndMcpAICompanion.Tests/Chat/DndChatServiceTests.cs`:

```csharp
using DndMcpAICompanion.Features.Chat;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DndMcpAICompanion.Tests.Chat;

file sealed class FakeChatClient : IChatClient
{
    public string Reply { get; set; } = "Test reply";
    public bool ShouldThrow { get; set; }

    public Task<ChatCompletion> CompleteAsync(
        IList<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (ShouldThrow) throw new HttpRequestException("Ollama unreachable");
        return Task.FromResult(new ChatCompletion([new ChatMessage(ChatRole.Assistant, Reply)]));
    }

    public IAsyncEnumerable<StreamingChatCompletionUpdate> CompleteStreamingAsync(
        IList<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public ChatClientMetadata Metadata => new("fake", null, null);
    public TService? GetService<TService>(object? key = null) where TService : class => null;
    public void Dispose() { }
}

public class DndChatServiceTests
{
    [Fact]
    public async Task SendAsync_appends_user_and_assistant_messages_to_history()
    {
        var client = new FakeChatClient { Reply = "Fireball deals 8d6 fire damage." };
        var svc = new DndChatService(client, []);

        var reply = await svc.SendAsync("What does fireball do?", CancellationToken.None);

        reply.Should().Be("Fireball deals 8d6 fire damage.");
        svc.History.Should().HaveCount(2);
        svc.History[0].Role.Should().Be(ChatRole.User);
        svc.History[0].Text.Should().Be("What does fireball do?");
        svc.History[1].Role.Should().Be(ChatRole.Assistant);
        svc.History[1].Text.Should().Be("Fireball deals 8d6 fire damage.");
    }

    [Fact]
    public async Task SendAsync_returns_error_message_when_ollama_is_unreachable()
    {
        var client = new FakeChatClient { ShouldThrow = true };
        var svc = new DndChatService(client, []);

        var reply = await svc.SendAsync("Hello", CancellationToken.None);

        reply.Should().Be("The AI is unavailable right now. Please try again.");
        svc.History.Should().HaveCount(2);
        svc.History[1].Role.Should().Be(ChatRole.Assistant);
    }
}
```

- [ ] **Step 3: Run tests to confirm they fail**

```bash
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj -v quiet 2>&1 | tail -5
```

Expected: compile error — `DndChatService` does not exist.

- [ ] **Step 4: Create DndChatService**

Create `DndMcpAICompanion/Features/Chat/DndChatService.cs`:

```csharp
using Microsoft.Extensions.AI;

namespace DndMcpAICompanion.Features.Chat;

public sealed class DndChatService(IChatClient chatClient, IReadOnlyList<AIFunction> tools)
{
    public List<ChatMessage> History { get; } = [];

    public async Task<string> SendAsync(string userMessage, CancellationToken ct)
    {
        History.Add(new ChatMessage(ChatRole.User, userMessage));
        try
        {
            var response = await chatClient.CompleteAsync(
                History,
                new ChatOptions { Tools = [.. tools] },
                ct);
            var reply = response.Message.Text ?? string.Empty;
            History.Add(new ChatMessage(ChatRole.Assistant, reply));
            return reply;
        }
        catch (Exception)
        {
            const string error = "The AI is unavailable right now. Please try again.";
            History.Add(new ChatMessage(ChatRole.Assistant, error));
            return error;
        }
    }
}
```

- [ ] **Step 5: Run tests to confirm they pass**

```bash
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj -v quiet 2>&1 | tail -5
```

Expected: `Passed! - Failed: 0, Passed: 2`.

- [ ] **Step 6: Commit**

```bash
git add DndMcpAICompanion/ DndMcpAICompanion.Tests/ DndMcpAICsharpFun.slnx
git commit -m "feat(companion): scaffold project, config options, DndChatService with tests"
```

---

### Task 4: Program.cs — Wire Everything Together

**Files:**
- Create: `DndMcpAICompanion/Program.cs`

- [ ] **Step 1: Create Program.cs**

Create `DndMcpAICompanion/Program.cs`:

```csharp
using DndMcpAICompanion.Features.Chat;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("Config/appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"Config/appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

var ollamaOpts = builder.Configuration.GetSection("Ollama").Get<OllamaOptions>()
    ?? throw new InvalidOperationException("Ollama configuration is missing.");
var mcpOpts = builder.Configuration.GetSection("Mcp").Get<McpClientOptions>()
    ?? throw new InvalidOperationException("Mcp configuration is missing.");

// AI layer — Ollama via Microsoft.Extensions.AI
builder.Services.AddSingleton<IChatClient>(
    new OllamaChatClient(new Uri(ollamaOpts.Url), ollamaOpts.Model));

// MCP client — connect to D&D knowledge server and fetch tools
var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(mcpOpts.Url),
    TransportMode = HttpTransportMode.StreamableHttp,
    AdditionalHeaders = new Dictionary<string, string>
    {
        ["X-Mcp-Api-Key"] = mcpOpts.ApiKey
    }
});
var mcpClient = await McpClient.CreateAsync(transport);
var mcpTools = await mcpClient.ListToolsAsync();
builder.Services.AddSingleton(mcpClient);
builder.Services.AddSingleton<IReadOnlyList<AIFunction>>(mcpTools.Cast<AIFunction>().ToList());

builder.Services.AddScoped<DndChatService>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<DndMcpAICompanion.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

- [ ] **Step 2: Build to confirm no errors**

```bash
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj -q 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s).` (Will warn about missing `App` component — that's fine, created in Task 5.)

---

### Task 5: Blazor Boilerplate Components

**Files:**
- Create: `DndMcpAICompanion/Components/App.razor`
- Create: `DndMcpAICompanion/Components/Routes.razor`
- Create: `DndMcpAICompanion/Components/Layout/MainLayout.razor`
- Create: `DndMcpAICompanion/wwwroot/app.css`
- Create: `DndMcpAICompanion/wwwroot/app.js`

- [ ] **Step 1: Create App.razor**

Create `DndMcpAICompanion/Components/App.razor`:

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>D&D Companion</title>
    <base href="/" />
    <link rel="stylesheet" href="app.css" />
    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
    <script src="app.js"></script>
</body>
</html>
```

- [ ] **Step 2: Create Routes.razor**

Create `DndMcpAICompanion/Components/Routes.razor`:

```razor
<Router AppAssembly="typeof(App).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)" />
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

- [ ] **Step 3: Create MainLayout.razor**

Create `DndMcpAICompanion/Components/Layout/MainLayout.razor`:

```razor
@inherits LayoutComponentBase

<main>
    @Body
</main>
```

- [ ] **Step 4: Create app.css**

Create `DndMcpAICompanion/wwwroot/app.css`:

```css
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

body {
    font-family: system-ui, sans-serif;
    background: #0f0f1a;
    color: #e0e0e0;
    height: 100vh;
    display: flex;
    flex-direction: column;
}

.chat-container {
    display: flex;
    flex-direction: column;
    height: 100vh;
    max-width: 860px;
    margin: 0 auto;
    width: 100%;
    padding: 0 16px;
}

.chat-header {
    padding: 16px 0;
    font-size: 18px;
    font-weight: 600;
    color: #a78bfa;
    border-bottom: 1px solid #2a2a3a;
}

.messages {
    flex: 1;
    overflow-y: auto;
    padding: 16px 0;
    display: flex;
    flex-direction: column;
    gap: 12px;
}

.message { display: flex; }
.message.user { justify-content: flex-end; }
.message.assistant { justify-content: flex-start; }

.bubble {
    max-width: 75%;
    padding: 10px 14px;
    border-radius: 12px;
    line-height: 1.5;
    white-space: pre-wrap;
    word-break: break-word;
}

.message.user .bubble { background: #4c3a8a; color: #f0e8ff; border-radius: 12px 12px 2px 12px; }
.message.assistant .bubble { background: #1e1e3a; color: #e0e0e0; border-radius: 12px 12px 12px 2px; }
.bubble.loading { color: #888; font-style: italic; }

.input-row {
    display: flex;
    gap: 8px;
    padding: 12px 0 20px;
    border-top: 1px solid #2a2a3a;
}

.input-row input {
    flex: 1;
    background: #1e1e2e;
    border: 1px solid #3a3a5a;
    border-radius: 8px;
    color: #e0e0e0;
    font-size: 15px;
    padding: 10px 14px;
    outline: none;
}

.input-row input:focus { border-color: #7c6aaa; }
.input-row input:disabled { opacity: 0.5; }

.input-row button {
    background: #5b3fa0;
    border: none;
    border-radius: 8px;
    color: white;
    cursor: pointer;
    font-size: 15px;
    padding: 10px 20px;
}

.input-row button:hover:not(:disabled) { background: #6d4ec0; }
.input-row button:disabled { opacity: 0.5; cursor: not-allowed; }
```

- [ ] **Step 5: Create app.js**

Create `DndMcpAICompanion/wwwroot/app.js`:

```js
window.scrollToBottom = (elementId) => {
    const el = document.getElementById(elementId);
    if (el) el.scrollTop = el.scrollHeight;
};
```

- [ ] **Step 6: Build to confirm**

```bash
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj -q 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s).`

---

### Task 6: Chat.razor Page

**Files:**
- Create: `DndMcpAICompanion/Components/Pages/Chat.razor`

- [ ] **Step 1: Create Chat.razor**

Create `DndMcpAICompanion/Components/Pages/Chat.razor`:

```razor
@page "/"
@rendermode InteractiveServer
@inject DndMcpAICompanion.Features.Chat.DndChatService ChatService
@inject IJSRuntime JS

<div class="chat-container">
    <div class="chat-header">⚔️ D&D Companion</div>

    <div class="messages" id="messages-container">
        @foreach (var msg in ChatService.History)
        {
            <div class="message @(msg.Role == Microsoft.Extensions.AI.ChatRole.User ? "user" : "assistant")">
                <div class="bubble">@msg.Text</div>
            </div>
        }
        @if (_loading)
        {
            <div class="message assistant">
                <div class="bubble loading">Thinking...</div>
            </div>
        }
    </div>

    <div class="input-row">
        <input @bind="_input"
               @bind:event="oninput"
               @onkeydown="OnKeyDown"
               disabled="@_loading"
               placeholder="Ask about D&D rules, spells, monsters..." />
        <button @onclick="Send" disabled="@_loading">Send</button>
    </div>
</div>

@code {
    private string _input = "";
    private bool _loading;

    protected override void OnInitialized()
    {
        if (ChatService.History.Count == 0)
            ChatService.History.Add(new Microsoft.Extensions.AI.ChatMessage(
                Microsoft.Extensions.AI.ChatRole.Assistant,
                "Hello, adventurer! Ask me anything about D&D rules, spells, monsters, and lore."));
    }

    private async Task Send()
    {
        var msg = _input.Trim();
        if (string.IsNullOrEmpty(msg) || _loading) return;

        _input = "";
        _loading = true;
        StateHasChanged();

        await ChatService.SendAsync(msg, CancellationToken.None);

        _loading = false;
        StateHasChanged();
        await JS.InvokeVoidAsync("scrollToBottom", "messages-container");
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey)
            await Send();
    }
}
```

- [ ] **Step 2: Build and run tests**

```bash
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj -q 2>&1 | tail -4
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj -q 2>&1 | tail -3
```

Expected: build succeeds, `Passed! - Failed: 0, Passed: 2`.

- [ ] **Step 3: Commit**

```bash
git add DndMcpAICompanion/
git commit -m "feat(companion): add Blazor boilerplate, Chat.razor page, Program.cs wiring"
```

---

### Task 7: Docker

**Files:**
- Create: `DndMcpAICompanion/Dockerfile`
- Modify: `docker-compose.yml`

- [ ] **Step 1: Create Dockerfile**

Create `DndMcpAICompanion/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY DndMcpAICompanion.csproj .
COPY . .
RUN dotnet restore

RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "DndMcpAICompanion.dll"]
```

- [ ] **Step 2: Add companion service to docker-compose.yml**

Open `docker-compose.yml`. Locate the `app:` service block. Add the `companion` service directly after it:

```yaml
  companion:
    build:
      context: ./DndMcpAICompanion
      dockerfile: Dockerfile
    ports:
      - "5102:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Development}
      - Mcp__ApiKey=${MCP_API_KEY:-devMcpKey}
    depends_on:
      app:
        condition: service_started
      ollama:
        condition: service_healthy
    networks:
      - dnd_net
    restart: unless-stopped
```

- [ ] **Step 3: Verify docker-compose config parses**

```bash
docker compose config --quiet 2>&1 | tail -5
```

Expected: no errors (silent success or just a blank line).

- [ ] **Step 4: Commit**

```bash
git add DndMcpAICompanion/Dockerfile docker-compose.yml
git commit -m "feat(companion): add Dockerfile and docker-compose service on port 5102"
```

---

### Task 8: Final Verification

- [ ] **Step 1: Run the full test suite**

```bash
dotnet test --no-build -q 2>&1 | tail -5
```

Expected: all tests pass (existing 358 + 2 new = 360 total).

- [ ] **Step 2: Build both projects clean**

```bash
dotnet build DndMcpAICsharpFun.slnx -q 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 3: Manual smoke test (requires stack running)**

Start the existing stack:
```bash
docker compose up app ollama qdrant -d
```

Then run the companion locally:
```bash
cd DndMcpAICompanion
dotnet run
```

Open http://localhost:5000 (or the port shown), type "What is a Fireball spell?", verify the AI responds using MCP tool results.
