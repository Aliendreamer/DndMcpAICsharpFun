# MCP Retrieval Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the D&D retrieval API as a Streamable HTTP MCP server at `/mcp`, protected by a dedicated `X-Mcp-Api-Key`, with three tools — `search_lore`, `search_entities`, and `get_entity`.

**Architecture:** A `Features/Mcp/` vertical slice contains `McpOptions` (config), `McpAuthMiddleware` (401 on missing/wrong key), and `DndMcpTools` (three `[McpServerTool]` methods injecting existing retrieval services). The official `ModelContextProtocol.AspNetCore` NuGet package handles the JSON-RPC protocol and Streamable HTTP transport. `WithToolsFromAssembly()` auto-discovers any future tool files with no registration changes.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, `ModelContextProtocol.AspNetCore`, `IRagRetrievalService`, `IEntityRetrievalService`, xUnit, FluentAssertions.

---

### Task 1: NuGet Package + Configuration

**Files:**

- Modify: `DndMcpAICsharpFun.csproj`
- Modify: `Config/appsettings.json`
- Modify: `Config/appsettings.Development.json`

- [ ] **Step 1: Add the MCP SDK package**

```bash
dotnet add package ModelContextProtocol.AspNetCore
```

Expected: package added, `dotnet build` succeeds.

- [ ] **Step 2: Add `Mcp` section to `Config/appsettings.json`**

Open `Config/appsettings.json` and add the `Mcp` block alongside the existing top-level sections:

```json
"Mcp": {
  "ApiKey": ""
}
```

- [ ] **Step 3: Add dev key to `Config/appsettings.Development.json`**

Open `Config/appsettings.Development.json` and add:

```json
"Mcp": {
  "ApiKey": "devMcpKey"
}
```

- [ ] **Step 4: Verify build**

```bash
dotnet build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

---

### Task 2: McpOptions + McpAuthMiddleware

**Files:**

- Create: `Features/Mcp/McpOptions.cs`
- Create: `Features/Mcp/McpAuthMiddleware.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Mcp/McpAuthMiddlewareTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `DndMcpAICsharpFun.Tests/Entities/Mcp/McpAuthMiddlewareTests.cs`:

```csharp
using DndMcpAICsharpFun.Features.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Mcp;

public class McpAuthMiddlewareTests
{
    private static McpAuthMiddleware BuildMiddleware(string configuredKey, out bool nextCalled)
    {
        var called = false;
        nextCalled = false;
        var next = new RequestDelegate(_ => { called = true; return Task.CompletedTask; });
        var mw = new McpAuthMiddleware(next, Options.Create(new McpOptions { ApiKey = configuredKey }));
        // capture ref via closure trick — caller reads the field after invocation
        return mw;
    }

    [Fact]
    public async Task Missing_key_returns_401()
    {
        var next = new RequestDelegate(_ => Task.CompletedTask);
        var mw = new McpAuthMiddleware(next, Options.Create(new McpOptions { ApiKey = "secret" }));
        var ctx = new DefaultHttpContext();

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Wrong_key_returns_401()
    {
        var nextCalled = false;
        var next = new RequestDelegate(_ => { nextCalled = true; return Task.CompletedTask; });
        var mw = new McpAuthMiddleware(next, Options.Create(new McpOptions { ApiKey = "secret" }));
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Mcp-Api-Key"] = "wrong";

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Correct_key_calls_next()
    {
        var nextCalled = false;
        var next = new RequestDelegate(_ => { nextCalled = true; return Task.CompletedTask; });
        var mw = new McpAuthMiddleware(next, Options.Create(new McpOptions { ApiKey = "secret" }));
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Mcp-Api-Key"] = "secret";

        await mw.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_configured_key_returns_401()
    {
        var next = new RequestDelegate(_ => Task.CompletedTask);
        var mw = new McpAuthMiddleware(next, Options.Create(new McpOptions { ApiKey = "" }));
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Mcp-Api-Key"] = "anything";

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test --filter "McpAuthMiddlewareTests" -v quiet
```

Expected: compile error — `McpOptions` and `McpAuthMiddleware` do not exist yet.

- [ ] **Step 3: Create `Features/Mcp/McpOptions.cs`**

```csharp
namespace DndMcpAICsharpFun.Features.Mcp;

public sealed class McpOptions
{
    public string ApiKey { get; init; } = string.Empty;
}
```

- [ ] **Step 4: Create `Features/Mcp/McpAuthMiddleware.cs`**

```csharp
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Mcp;

public sealed class McpAuthMiddleware(RequestDelegate next, IOptions<McpOptions> options)
{
    private readonly string _apiKey = options.Value.ApiKey;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (string.IsNullOrEmpty(_apiKey) ||
            !ctx.Request.Headers.TryGetValue("X-Mcp-Api-Key", out var key) ||
            key != _apiKey)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await next(ctx);
    }
}
```

- [ ] **Step 5: Run tests to confirm they pass**

```bash
dotnet test --filter "McpAuthMiddlewareTests" -v quiet
```

Expected: `4 passed`.

- [ ] **Step 6: Commit**

```bash
git add Features/Mcp/McpOptions.cs Features/Mcp/McpAuthMiddleware.cs \
        DndMcpAICsharpFun.Tests/Entities/Mcp/McpAuthMiddlewareTests.cs \
        Config/appsettings.json Config/appsettings.Development.json \
        DndMcpAICsharpFun.csproj
git commit -m "feat(mcp): add McpOptions, McpAuthMiddleware, NuGet package, config"
```

---

### Task 3: DndMcpTools

**Files:**

- Create: `Features/Mcp/DndMcpTools.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Mcp/DndMcpToolsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `DndMcpAICsharpFun.Tests/Entities/Mcp/DndMcpToolsTests.cs`:

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Mcp;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Mcp;

// ── Fakes ────────────────────────────────────────────────────────────────────

file sealed class FakeRagService : IRagRetrievalService
{
    public IList<RetrievalResult> Results { get; set; } = [];

    public Task<IList<RetrievalResult>> SearchAsync(RetrievalQuery query, CancellationToken ct = default)
        => Task.FromResult(Results);

    public Task<IList<RetrievalDiagnosticResult>> SearchDiagnosticAsync(RetrievalQuery query, CancellationToken ct = default)
        => Task.FromResult<IList<RetrievalDiagnosticResult>>([]);
}

file sealed class FakeEntityService : IEntityRetrievalService
{
    public EntityFullResult? GetResult { get; set; }
    public IList<EntitySearchResult> SearchResults { get; set; } = [];

    public Task<EntityFullResult?> GetByIdAsync(string id, CancellationToken ct)
        => Task.FromResult(GetResult);

    public Task<IList<EntitySearchResult>> SearchAsync(EntitySearchQuery query, CancellationToken ct)
        => Task.FromResult(SearchResults);

    public Task<IList<EntityDiagnosticResult>> SearchDiagnosticAsync(EntitySearchQuery query, CancellationToken ct)
        => Task.FromResult<IList<EntityDiagnosticResult>>([]);
}

// ── Tests ────────────────────────────────────────────────────────────────────

public class DndMcpToolsTests
{
    private static ChunkMetadata Metadata(string sourceBook = "PHB") =>
        new(sourceBook, DndVersion.Edition2014, ContentCategory.Spell, null, "Spells", 150, 0);

    [Fact]
    public async Task search_lore_returns_json_with_results()
    {
        var fakeRag = new FakeRagService
        {
            Results = [new RetrievalResult("Fireball deals 8d6 fire damage.", Metadata(), 0.95f)]
        };
        var tools = new DndMcpTools(fakeRag, new FakeEntityService());

        var result = await tools.search_lore("fireball");

        result.Should().Contain("Fireball");
        result.Should().Contain("PHB");
        result.Should().Contain("0.95");
    }

    [Fact]
    public async Task search_lore_with_no_results_returns_message()
    {
        var tools = new DndMcpTools(new FakeRagService(), new FakeEntityService());

        var result = await tools.search_lore("xyzzy");

        result.Should().Be("No lore results found.");
    }

    [Fact]
    public async Task search_lore_with_unknown_version_returns_gracefully()
    {
        var tools = new DndMcpTools(new FakeRagService(), new FakeEntityService());

        var result = await tools.search_lore("fireball", version: "Edition9999");

        result.Should().Be("No lore results found.");
    }

    [Fact]
    public async Task search_entities_returns_json_with_results()
    {
        var fakeEntity = new FakeEntityService
        {
            SearchResults =
            [
                new EntitySearchResult("phb.spell.fireball", EntityType.Spell, "Fireball",
                    "PHB", "Edition2014", null, [], "8d6 fire damage", 0.97f)
            ]
        };
        var tools = new DndMcpTools(new FakeRagService(), fakeEntity);

        var result = await tools.search_entities("fireball");

        result.Should().Contain("phb.spell.fireball");
        result.Should().Contain("Fireball");
        result.Should().Contain("Spell");
    }

    [Fact]
    public async Task search_entities_with_no_results_returns_message()
    {
        var tools = new DndMcpTools(new FakeRagService(), new FakeEntityService());

        var result = await tools.search_entities("xyzzy");

        result.Should().Be("No entities found.");
    }

    [Fact]
    public async Task search_entities_with_unknown_type_returns_gracefully()
    {
        var tools = new DndMcpTools(new FakeRagService(), new FakeEntityService());

        var result = await tools.search_entities("fireball", type: "NotARealType");

        result.Should().Be("No entities found.");
    }

    [Fact]
    public async Task get_entity_returns_json_for_known_id()
    {
        var envelope = new EntityEnvelope(
            Id: "phb.spell.fireball",
            Type: EntityType.Spell,
            Name: "Fireball",
            SourceBook: "PHB",
            Edition: "Edition2014",
            Page: 241,
            FirstAppearedIn: new FirstAppearance("PHB", "Edition2014"),
            RevisedIn: [],
            SettingTags: [],
            CanonicalText: "A bright streak flashes...",
            Fields: default);

        var fakeEntity = new FakeEntityService { GetResult = new EntityFullResult(envelope) };
        var tools = new DndMcpTools(new FakeRagService(), fakeEntity);

        var result = await tools.get_entity("phb.spell.fireball");

        result.Should().Contain("phb.spell.fireball");
        result.Should().Contain("Fireball");
        result.Should().Contain("A bright streak");
    }

    [Fact]
    public async Task get_entity_returns_not_found_message_for_unknown_id()
    {
        var tools = new DndMcpTools(new FakeRagService(), new FakeEntityService());

        var result = await tools.get_entity("fake.id.does-not-exist");

        result.Should().Be("Entity not found: fake.id.does-not-exist");
    }
}
```

- [ ] **Step 2: Run tests to confirm compile error**

```bash
dotnet test --filter "DndMcpToolsTests" -v quiet
```

Expected: compile error — `DndMcpTools` does not exist.

- [ ] **Step 3: Create `Features/Mcp/DndMcpTools.cs`**

```csharp
using System.ComponentModel;
using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using ModelContextProtocol.Server;

namespace DndMcpAICsharpFun.Features.Mcp;

[McpServerToolType]
public sealed class DndMcpTools(IRagRetrievalService ragService, IEntityRetrievalService entityService)
{
    private static readonly JsonSerializerOptions _json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool, Description(
        "Search D&D rules, lore, and narrative text using semantic similarity. " +
        "Use for rules lookups, how-does-X-work questions, and prose descriptions.")]
    public async Task<string> search_lore(
        [Description("Natural language question or keyword")] string query,
        [Description("Edition filter: Edition2014 or Edition2024")] string? version = null,
        [Description("Content category: Spell, Monster, Class, Race, Background, Item, Rule, " +
                     "Combat, Adventuring, Condition, God, Plane, Treasure, Encounter, Trap, Trait, Lore")]
        string? category = null,
        [Description("Maximum number of results (default 5)")] int topK = 5,
        CancellationToken ct = default)
    {
        var dndVersion = Enum.TryParse<DndVersion>(version, out var v) ? v : (DndVersion?)null;
        var contentCategory = Enum.TryParse<ContentCategory>(category, out var c) ? c : (ContentCategory?)null;

        var results = await ragService.SearchAsync(
            new RetrievalQuery(query, dndVersion, contentCategory, TopK: topK), ct);

        if (results.Count == 0)
            return "No lore results found.";

        return JsonSerializer.Serialize(results.Select(r => new
        {
            title = r.Metadata.SectionTitle ?? r.Metadata.Chapter,
            text = r.Text,
            sourceBook = r.Metadata.SourceBook,
            category = r.Metadata.Category.ToString(),
            score = r.Score
        }), _json);
    }

    [McpServerTool, Description(
        "Search structured D&D entities: spells, monsters, classes, subclasses, items, feats, races, and more. " +
        "Use for stat lookups, finding entities by type, or filtering by CR, spell level, or keywords.")]
    public async Task<string> search_entities(
        [Description("Search text — entity name or description")] string query,
        [Description("Entity type: Spell, Monster, Class, Subclass, Race, Subrace, Background, Feat, " +
                     "Weapon, Armor, Item, MagicItem, Trap, God, Plane, Faction, Location, Condition, Lore, Rule")]
        string? type = null,
        [Description("Edition: Edition2014 or Edition2024")] string? edition = null,
        [Description("Trait tag keyword e.g. Amphibious, Pack Tactics, Undead Fortitude")] string? keyword = null,
        [Description("Maximum challenge rating inclusive (for monsters)")] double? crMax = null,
        [Description("Spell level 0–9 (for spells)")] int? spellLevel = null,
        [Description("Restrict to SRD 5.1 entities only")] bool? srd = null,
        [Description("Restrict to SRD 5.2.1 entities only")] bool? srd52 = null,
        [Description("Number of results (default 10)")] int topK = 10,
        CancellationToken ct = default)
    {
        var entityType = Enum.TryParse<EntityType>(type, out var t) ? t : (EntityType?)null;

        var results = await entityService.SearchAsync(
            new EntitySearchQuery(
                QueryText: query,
                Type: entityType,
                SourceBook: null,
                Edition: edition,
                BookType: null,
                SettingTag: null,
                Keyword: keyword,
                CrNumericLte: crMax,
                CrNumericGte: null,
                SpellLevel: spellLevel,
                DamageType: null,
                TopK: topK,
                Srd: srd,
                Srd52: srd52), ct);

        if (results.Count == 0)
            return "No entities found.";

        return JsonSerializer.Serialize(results.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            type = r.Type.ToString(),
            sourceBook = r.SourceBook,
            edition = r.Edition,
            snippet = r.Snippet,
            score = r.Score
        }), _json);
    }

    [McpServerTool, Description(
        "Fetch a single D&D entity by its canonical ID (e.g. 'phb.spell.fireball', " +
        "'tce.subclass.circle-of-spores'). Use after search_entities to get full details.")]
    public async Task<string> get_entity(
        [Description("Canonical entity ID from a previous search_entities result")] string id,
        CancellationToken ct = default)
    {
        var result = await entityService.GetByIdAsync(id, ct);
        if (result is null)
            return $"Entity not found: {id}";

        var e = result.Envelope;
        return JsonSerializer.Serialize(new
        {
            id = e.Id,
            name = e.Name,
            type = e.Type.ToString(),
            sourceBook = e.SourceBook,
            edition = e.Edition,
            canonicalText = e.CanonicalText,
            keywords = e.Keywords,
            srd = e.Srd,
            srd52 = e.Srd52,
            fields = e.Fields
        }, _json);
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test --filter "DndMcpToolsTests" -v quiet
```

Expected: `7 passed`.

- [ ] **Step 5: Commit**

```bash
git add Features/Mcp/DndMcpTools.cs \
        DndMcpAICsharpFun.Tests/Entities/Mcp/DndMcpToolsTests.cs
git commit -m "feat(mcp): add DndMcpTools with search_lore, search_entities, get_entity"
```

---

### Task 4: Registration in Program.cs

**Files:**

- Modify: `Program.cs`

- [ ] **Step 1: Add using and options registration**

Open `Program.cs`. Add to the using block at the top:

```csharp
using DndMcpAICsharpFun.Features.Mcp;
using ModelContextProtocol.Server;
```

In the `// Options` block (alongside other `Configure` calls), add:

```csharp
builder.Services.Configure<McpOptions>(builder.Configuration.GetSection("Mcp"));
```

- [ ] **Step 2: Register the MCP server**

After `builder.Services.AddObservability(...)`, add:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();
```

- [ ] **Step 3: Add the auth guard and map the endpoint**

In the endpoint mapping section of `Program.cs`, before `app.Run()`, add:

```csharp
// MCP — guard /mcp with key check, then map the MCP endpoint
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseMiddleware<McpAuthMiddleware>());
app.MapMcp("/mcp");
```

- [ ] **Step 4: Build and run all tests**

```bash
dotnet build && dotnet test -v quiet
```

Expected: `Build succeeded` and all tests pass (346+ passing).

- [ ] **Step 5: Commit**

```bash
git add Program.cs
git commit -m "feat(mcp): register MCP server and map /mcp endpoint with auth guard"
```

---

### Task 5: HTTP Collection Updates

**Files:**

- Modify: `DndMcpAICsharpFun.http`
- Modify: `dnd-mcp-api.insomnia.json`

- [ ] **Step 1: Add MCP section to `DndMcpAICsharpFun.http`**

Open `DndMcpAICsharpFun.http`. After the `@adminKey` variable declaration at the top, add:

```
@mcpKey = devMcpKey
```

Then add a new section after the health endpoints block:

```
###

# ── MCP Server ───────────────────────────────────────────────────────────────
# The /mcp endpoint uses the Model Context Protocol (Streamable HTTP transport).
# It speaks JSON-RPC 2.0, not REST — use an MCP client, not curl.
# Configure AI clients with:
#   URL:    http://localhost:5101/mcp
#   Header: X-Mcp-Api-Key: devMcpKey
# Available tools: search_lore, search_entities, get_entity
#
# Verify the endpoint is alive (returns 405 Method Not Allowed for GET — that's correct):
GET {{baseUrl}}/mcp
X-Mcp-Api-Key: {{mcpKey}}
```

- [ ] **Step 2: Add MCP entry to `dnd-mcp-api.insomnia.json`**

Open `dnd-mcp-api.insomnia.json`. Locate the `resources` array. Add a new request object for the MCP endpoint alongside the existing entries (copy the shape of an existing GET request and adjust):

```json
{
  "_id": "req_mcp_check",
  "_type": "request",
  "parentId": "<use the same parentId as adjacent requests>",
  "name": "MCP — Endpoint check (GET → 405 is correct)",
  "method": "GET",
  "url": "{{ _.baseUrl }}/mcp",
  "headers": [
    { "name": "X-Mcp-Api-Key", "value": "devMcpKey" }
  ],
  "body": {}
}
```

- [ ] **Step 3: Run full test suite one final time**

```bash
dotnet test -v quiet
```

Expected: all tests pass.

- [ ] **Step 4: Final commit**

```bash
git add DndMcpAICsharpFun.http dnd-mcp-api.insomnia.json
git commit -m "docs(mcp): add /mcp endpoint notes to .http and .insomnia.json"
```
