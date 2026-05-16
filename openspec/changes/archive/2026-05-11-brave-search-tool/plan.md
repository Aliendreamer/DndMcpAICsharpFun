# SearXNG Web Search Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `search_web` MCP tool backed by a local SearXNG instance, gated by a checkbox in the Blazor companion UI so the AI only searches the web when the user explicitly enables it.

**Architecture:** `SearXNGClient` is a typed `HttpClient` that queries a local SearXNG Docker service, post-filters results by a configurable domain allowlist, and returns empty on failure. `SearchWebTool` exposes it as an MCP tool. `DndChatService.SendAsync` gains a `bool allowWebSearch` parameter; when false it strips `search_web` from the active tool list before calling the AI. `Chat.razor` adds a checkbox that controls that bool.

**Tech Stack:** `System.Net.Http.Json` (already in SDK), `Microsoft.Extensions.AI` (AIFunction/AIFunctionFactory), `[McpServerToolType]` (ModelContextProtocol.Server), SearXNG Docker image, FluentAssertions + xUnit (tests).

---

## File Map

**Create:**

- `Features/Search/SearXNGOptions.cs` — config record (Url, MaxResults, AllowedDomains)
- `Features/Search/SearXNGResult.cs` — result record (Title, Url, Snippet)
- `Features/Search/SearXNGClient.cs` — typed HttpClient with domain filtering
- `Features/Search/SearchWebTool.cs` — `[McpServerToolType]` with `search_web` method
- `infra/searxng/settings.yml` — minimal SearXNG config (JSON format, no rate limit)
- `DndMcpAICsharpFun.Tests/Search/SearXNGClientTests.cs` — unit tests for client

**Modify:**

- `Extensions/ServiceCollectionExtensions.cs` — add `AddWebSearch()` extension
- `Config/appsettings.json` — add `"SearXNG"` section
- `docker-compose.yml` — add `searxng` service, add it to `app` depends_on
- `DndMcpAICompanion/Features/Chat/DndChatService.cs` — add `bool allowWebSearch` to `SendAsync`
- `DndMcpAICompanion/Components/Pages/Chat.razor` — add checkbox, pass state to SendAsync
- `DndMcpAICompanion.Tests/Chat/DndChatServiceTests.cs` — update existing calls + add filter tests

---

## Task 1: SearXNG Client (TDD)

**Files:**

- Create: `Features/Search/SearXNGOptions.cs`
- Create: `Features/Search/SearXNGResult.cs`
- Create: `Features/Search/SearXNGClient.cs`
- Create: `DndMcpAICsharpFun.Tests/Search/SearXNGClientTests.cs`

- [ ] **Step 1.1: Create the test file with three failing tests**

Create `DndMcpAICsharpFun.Tests/Search/SearXNGClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using DndMcpAICsharpFun.Features.Search;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Search;

internal sealed class FakeMessageHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
}

public sealed class SearXNGClientTests
{
    private static SearXNGClient Build(string json, HttpStatusCode status = HttpStatusCode.OK,
        string[]? allowedDomains = null)
    {
        var handler = new FakeMessageHandler(json, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://searxng:8080") };
        var opts = Options.Create(new SearXNGOptions
        {
            Url = "http://searxng:8080",
            MaxResults = 5,
            AllowedDomains = allowedDomains ?? ["dndbeyond.com", "5etools.com"]
        });
        return new SearXNGClient(http, opts, NullLogger<SearXNGClient>.Instance);
    }

    private static string MakeJson(params (string title, string url, string content)[] results)
    {
        var items = string.Join(",", results.Select(r =>
            $$"""{"title":"{{r.title}}","url":"{{r.url}}","content":"{{r.content}}"}"""));
        return $$"""{"results":[{{items}}]}""";
    }

    [Fact]
    public async Task SearchAsync_ReturnsMatchingDomainResults()
    {
        var json = MakeJson(
            ("Fireball", "https://dndbeyond.com/spells/fireball", "8d6 fire damage"),
            ("Some Blog", "https://randomblog.com/fireball", "random post"));
        var client = Build(json);

        var results = await client.SearchAsync("fireball", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Fireball", results[0].Title);
        Assert.Equal("https://dndbeyond.com/spells/fireball", results[0].Url);
        Assert.Equal("8d6 fire damage", results[0].Snippet);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_WhenNoDomainMatches()
    {
        var json = MakeJson(("Some Blog", "https://randomblog.com/fireball", "text"));
        var client = Build(json);

        var results = await client.SearchAsync("fireball", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_OnHttpFailure()
    {
        var client = Build("{}", HttpStatusCode.ServiceUnavailable);

        var results = await client.SearchAsync("fireball", CancellationToken.None);

        Assert.Empty(results);
    }
}
```

- [ ] **Step 1.2: Run tests to confirm they fail (types not found)**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj \
  --filter "FullyQualifiedName~SearXNGClientTests" 2>&1 | tail -10
```

Expected: build error — `SearXNGClient`, `SearXNGOptions` not found.

- [ ] **Step 1.3: Create `Features/Search/SearXNGOptions.cs`**

```csharp
namespace DndMcpAICsharpFun.Features.Search;

public sealed class SearXNGOptions
{
    public string Url { get; init; } = "http://searxng:8080";
    public int MaxResults { get; init; } = 5;
    public string[] AllowedDomains { get; init; } = [];
}
```

- [ ] **Step 1.4: Create `Features/Search/SearXNGResult.cs`**

```csharp
namespace DndMcpAICsharpFun.Features.Search;

public sealed record SearXNGResult(string Title, string Url, string Snippet);
```

- [ ] **Step 1.5: Create `Features/Search/SearXNGClient.cs`**

```csharp
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Search;

public sealed class SearXNGClient(
    HttpClient httpClient,
    IOptions<SearXNGOptions> options,
    ILogger<SearXNGClient> logger)
{
    private readonly SearXNGOptions _opts = options.Value;

    public async Task<IReadOnlyList<SearXNGResult>> SearchAsync(string query, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(query);
            var url = $"/search?q={encoded}&format=json&language=en";
            var response = await httpClient.GetFromJsonAsync<SearXNGResponse>(url, ct);
            if (response?.Results is null) return [];

            return response.Results
                .Where(r => _opts.AllowedDomains.Length == 0 ||
                            _opts.AllowedDomains.Any(d =>
                                r.Url?.Contains(d, StringComparison.OrdinalIgnoreCase) == true))
                .Take(_opts.MaxResults)
                .Select(r => new SearXNGResult(r.Title ?? "", r.Url ?? "", r.Content ?? ""))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning("SearXNG search failed: {Message}", ex.Message);
            return [];
        }
    }

    private sealed record SearXNGResponse(
        [property: JsonPropertyName("results")] List<SearXNGRaw>? Results);

    private sealed record SearXNGRaw(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("content")] string? Content);
}
```

- [ ] **Step 1.6: Run tests — all three must pass**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj \
  --filter "FullyQualifiedName~SearXNGClientTests" 2>&1 | tail -8
```

Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 1.7: Full test suite — no regressions**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --no-build 2>&1 | tail -5
```

Expected: all previously passing tests still pass.

- [ ] **Step 1.8: Commit**

```bash
git add Features/Search/ DndMcpAICsharpFun.Tests/Search/
git commit -m "feat(search): add SearXNGClient with domain filtering"
```

---

## Task 2: SearchWebTool + Service Registration + Config

**Files:**

- Create: `Features/Search/SearchWebTool.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Modify: `Config/appsettings.json`
- Modify: `Program.cs`

- [ ] **Step 2.1: Create `Features/Search/SearchWebTool.cs`**

```csharp
using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace DndMcpAICsharpFun.Features.Search;

[McpServerToolType]
public sealed class SearchWebTool(SearXNGClient searxng)
{
    private static readonly JsonSerializerOptions _json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool, Description(
        "Search the live web for D&D rules, lore, or community discussions not found in local books. " +
        "Only call this when the user explicitly asks to search the web.")]
    public async Task<string> search_web(
        [Description("Search query")] string query,
        CancellationToken ct = default)
    {
        var results = await searxng.SearchAsync(query, ct);
        if (results.Count == 0)
            return "No web results found.";

        return JsonSerializer.Serialize(results.Select(r => new
        {
            title = r.Title,
            url = r.Url,
            snippet = r.Snippet
        }), _json);
    }
}
```

- [ ] **Step 2.2: Add `AddWebSearch()` to `Extensions/ServiceCollectionExtensions.cs`**

Add `using DndMcpAICsharpFun.Features.Search;` to the top of the file with the other usings.

Add this method after `AddRetrieval()`:

```csharp
internal static IServiceCollection AddWebSearch(
    this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<SearXNGOptions>(configuration.GetSection("SearXNG"));
    services.AddHttpClient<SearXNGClient>((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<SearXNGOptions>>().Value;
        client.BaseAddress = new Uri(opts.Url);
        client.Timeout = TimeSpan.FromSeconds(10);
    });
    return services;
}
```

- [ ] **Step 2.3: Call `AddWebSearch()` in `Program.cs`**

After the line `builder.Services.AddRetrieval();`, add:

```csharp
builder.Services.AddWebSearch(builder.Configuration);
```

- [ ] **Step 2.4: Add `SearXNG` section to `Config/appsettings.json`**

After the `"Reranker"` closing brace, add:

```json
"SearXNG": {
    "Url": "http://searxng:8080",
    "MaxResults": 5,
    "AllowedDomains": [
        "dndbeyond.com",
        "5etools.com",
        "reddit.com",
        "enworld.org",
        "roll20.net",
        "sageadvice.eu"
    ]
}
```

- [ ] **Step 2.5: Build to verify**

```bash
dotnet build DndMcpAICsharpFun.csproj 2>&1 | tail -8
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 2.6: Commit**

```bash
git add Features/Search/SearchWebTool.cs Extensions/ServiceCollectionExtensions.cs \
  Config/appsettings.json Program.cs
git commit -m "feat(search): add search_web MCP tool and SearXNG service registration"
```

---

## Task 3: Docker Compose + SearXNG Settings

**Files:**

- Create: `infra/searxng/settings.yml`
- Modify: `docker-compose.yml`

- [ ] **Step 3.1: Create `infra/searxng/settings.yml`**

```bash
mkdir -p infra/searxng
```

Write `infra/searxng/settings.yml`:

```yaml
use_default_settings: true

server:
  secret_key: "dnd-companion-local-key"
  limiter: false

search:
  formats:
    - html
    - json

```

- [ ] **Step 3.2: Add `searxng` service to `docker-compose.yml`**

Add after the `sqlite-web` block, before `volumes:`:

```yaml
  searxng:
    image: searxng/searxng:latest
    container_name: searxng
    ports:
      - "8888:8080"
    volumes:
      - ./infra/searxng/settings.yml:/etc/searxng/settings.yml:ro
    networks:
      - dnd_net
    restart: unless-stopped
```

- [ ] **Step 3.3: Add `searxng` to `app` service `depends_on`**

In the `app` service, inside `depends_on:`, add:

```yaml
      searxng:
        condition: service_started
```

- [ ] **Step 3.4: Commit**

```bash
git add infra/searxng/settings.yml docker-compose.yml
git commit -m "feat(search): add SearXNG Docker Compose service"
```

---

## Task 4: DndChatService — allowWebSearch (TDD)

**Files:**

- Modify: `DndMcpAICompanion.Tests/Chat/DndChatServiceTests.cs`
- Modify: `DndMcpAICompanion/Features/Chat/DndChatService.cs`

- [ ] **Step 4.1: Replace `DndMcpAICompanion.Tests/Chat/DndChatServiceTests.cs`**

```csharp
using DndMcpAICompanion.Features.Chat;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Xunit;

namespace DndMcpAICompanion.Tests.Chat;

internal sealed class FakeChatClient : IChatClient
{
    public string Reply { get; set; } = "Test reply";
    public bool ShouldThrow { get; set; }
    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        if (ShouldThrow) throw new HttpRequestException("Ollama unreachable");
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public object? GetService(Type serviceType, object? key = null) => null;
    public void Dispose() { }
}

internal sealed class NullHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
}

public class DndChatServiceTests
{
    private static DndChatService CreateService(FakeChatClient client,
        IReadOnlyList<AITool>? tools = null) =>
        new(client, tools ?? [], new NullHttpContextAccessor(), new ChatRateLimiter(1000));

    [Fact]
    public async Task SendAsync_appends_user_and_assistant_messages_to_history()
    {
        var client = new FakeChatClient { Reply = "Fireball deals 8d6 fire damage." };
        var svc = CreateService(client);

        var reply = await svc.SendAsync("What does fireball do?", false, CancellationToken.None);

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
        var svc = CreateService(client);

        var reply = await svc.SendAsync("Hello", false, CancellationToken.None);

        reply.Should().Be("The AI is unavailable right now. Please try again.");
        svc.History.Should().HaveCount(2);
        svc.History[1].Role.Should().Be(ChatRole.Assistant);
    }

    [Fact]
    public async Task SendAsync_excludes_search_web_tool_when_web_search_disabled()
    {
        var client = new FakeChatClient();
        var searchWeb = AIFunctionFactory.Create(() => "result", "search_web");
        var searchLore = AIFunctionFactory.Create(() => "result", "search_lore");
        var svc = CreateService(client, [searchWeb, searchLore]);

        await svc.SendAsync("Hello", allowWebSearch: false, CancellationToken.None);

        var activeTools = client.LastOptions?.Tools ?? [];
        activeTools.Should().ContainSingle();
        activeTools.OfType<AIFunction>()
            .Should().ContainSingle(t => t.Metadata.Name == "search_lore");
        activeTools.OfType<AIFunction>()
            .Should().NotContain(t => t.Metadata.Name == "search_web");
    }

    [Fact]
    public async Task SendAsync_includes_search_web_tool_when_web_search_enabled()
    {
        var client = new FakeChatClient();
        var searchWeb = AIFunctionFactory.Create(() => "result", "search_web");
        var searchLore = AIFunctionFactory.Create(() => "result", "search_lore");
        var svc = CreateService(client, [searchWeb, searchLore]);

        await svc.SendAsync("Hello", allowWebSearch: true, CancellationToken.None);

        var activeTools = client.LastOptions?.Tools ?? [];
        activeTools.Should().HaveCount(2);
        activeTools.OfType<AIFunction>()
            .Should().Contain(t => t.Metadata.Name == "search_web");
    }
}
```

- [ ] **Step 4.2: Build to confirm failure (SendAsync signature not updated yet)**

```bash
dotnet build DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj 2>&1 | tail -8
```

Expected: error CS1501 — `SendAsync` has wrong number of arguments.

- [ ] **Step 4.3: Replace `DndMcpAICompanion/Features/Chat/DndChatService.cs`**

```csharp
using Microsoft.Extensions.AI;

namespace DndMcpAICompanion.Features.Chat;

public sealed class DndChatService(
    IChatClient chatClient,
    IReadOnlyList<AITool> tools,
    IHttpContextAccessor httpContextAccessor,
    ChatRateLimiter rateLimiter)
{
    public List<ChatMessage> History { get; } = [];

    public async Task<string> SendAsync(string userMessage, bool allowWebSearch, CancellationToken ct)
    {
        var ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!rateLimiter.TryAcquire(ip))
        {
            const string limited = "You're sending messages too quickly. Please wait a moment.";
            History.Add(new ChatMessage(ChatRole.User, userMessage));
            History.Add(new ChatMessage(ChatRole.Assistant, limited));
            return limited;
        }

        var activeTools = allowWebSearch
            ? tools
            : tools.Where(t => t is not AIFunction fn || fn.Metadata.Name != "search_web").ToList();

        History.Add(new ChatMessage(ChatRole.User, userMessage));
        try
        {
            var response = await chatClient.GetResponseAsync(
                History,
                new ChatOptions { Tools = [.. activeTools] },
                ct);
            var reply = response.Text ?? string.Empty;
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

- [ ] **Step 4.4: Run companion tests — all four must pass**

```bash
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj 2>&1 | tail -8
```

Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 4.5: Commit**

```bash
git add DndMcpAICompanion/Features/Chat/DndChatService.cs \
  DndMcpAICompanion.Tests/Chat/DndChatServiceTests.cs
git commit -m "feat(search): gate search_web behind allowWebSearch in DndChatService"
```

---

## Task 5: Chat.razor — Web Search Checkbox

**Files:**

- Modify: `DndMcpAICompanion/Components/Pages/Chat.razor`

- [ ] **Step 5.1: Replace `DndMcpAICompanion/Components/Pages/Chat.razor`**

```razor
@page "/"
@rendermode InteractiveServer
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.Extensions.AI
@attribute [Authorize]
@inject DndMcpAICompanion.Features.Chat.DndChatService ChatService
@inject IJSRuntime JS
@inject NavigationManager Nav

<div class="chat-container">
    <div class="chat-header">
        <span>⚔️ D&D Companion</span>
        <div class="header-user">
            <AuthorizeView>
                <Authorized>
                    <span class="username">@context.User.Identity!.Name</span>
                </Authorized>
            </AuthorizeView>
            <button class="logout-btn" @onclick="Logout">Logout</button>
        </div>
    </div>

    <div class="messages" id="messages-container">
        @foreach (var msg in ChatService.History)
        {
            <div class="message @(msg.Role == ChatRole.User ? "user" : "assistant")">
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
        <label class="web-search-toggle">
            <input type="checkbox" @bind="_webSearchEnabled" disabled="@_loading" />
            Search web
        </label>
        <button @onclick="Send" disabled="@_loading">Send</button>
    </div>
</div>

@code {
    private string _input = "";
    private bool _loading;
    private bool _webSearchEnabled;

    protected override void OnInitialized()
    {
        if (ChatService.History.Count == 0)
            ChatService.History.Add(new ChatMessage(
                ChatRole.Assistant,
                "Hello, adventurer! Ask me anything about D&D rules, spells, monsters, and lore."));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await JS.InvokeVoidAsync("scrollToBottom", "messages-container");
    }

    private async Task Send()
    {
        var msg = _input.Trim();
        if (string.IsNullOrEmpty(msg) || _loading) return;

        _input = "";
        _loading = true;
        StateHasChanged();

        try
        {
            await ChatService.SendAsync(msg, _webSearchEnabled, CancellationToken.None);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey)
            await Send();
    }

    private void Logout() => Nav.NavigateTo("/logout", forceLoad: true);
}
```

- [ ] **Step 5.2: Build the companion**

```bash
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj 2>&1 | tail -8
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5.3: Run both test suites**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --no-build 2>&1 | tail -5
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj --no-build 2>&1 | tail -5
```

Expected: both pass with 0 failures.

- [ ] **Step 5.4: Commit**

```bash
git add DndMcpAICompanion/Components/Pages/Chat.razor
git commit -m "feat(search): add web search checkbox to companion chat UI"
```
