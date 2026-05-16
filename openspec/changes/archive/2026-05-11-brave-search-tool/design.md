## Context

The MCP server already exposes `search_lore`, `search_entities`, and `get_entity` tools. Adding `search_web` follows the same pattern: a new `[McpServerToolType]` class registered at startup. The companion picks up all tools via `ListToolsAsync()` at startup and holds them as a singleton.

The companion Blazor UI controls whether `search_web` is available to the AI per message via a checkbox. When unchecked, the tool is stripped from the active list before the AI call — the AI cannot call it. This is purely client-side filtering; the MCP server always has the tool registered.

SearXNG is a self-hosted metasearch engine that aggregates Google, Bing, DuckDuckGo, and others. It exposes a JSON REST API with no authentication. It runs as a Docker Compose service on the existing `dnd_net` network.

## Goals / Non-Goals

**Goals:**

- Add a `search_web(query)` MCP tool backed by a local SearXNG instance
- Filter results to a configurable allowlist of D&D-relevant domains
- Return empty when no results match the domain filter (no unfiltered fallback)
- Graceful degradation when SearXNG is unreachable (return empty, log warning, no crash)
- Companion checkbox controls whether the AI can call `search_web` — unchecked means the tool is not in the active list for that call

**Non-Goals:**

- Caching or deduplicating web results
- Full-page content fetching (snippets only)
- Any changes to existing MCP tools or the RAG pipeline
- Configuring SearXNG upstream search engines (use SearXNG defaults)

## Architecture

```
Chat.razor
  _webSearchEnabled (bool, checkbox)
       │
       ▼
DndChatService.SendAsync(message, allowWebSearch)
  filters tools: removes search_web when allowWebSearch=false
       │
       ▼
IChatClient (AI model via Ollama)
  tool call: search_web(query)
       │
       ▼
SearchWebTool.search_web(query)
       │
       ▼
SearXNGClient.SearchAsync(query)
  GET http://searxng:8080/search?q=<query>&format=json&language=en
  post-filter: keep results where URL contains an AllowedDomain
  returns: IReadOnlyList<SearXNGResult> (empty if no domain match)
```

## Decisions

**Checkbox in companion UI, not tool description gate** — The tool is stripped from the active list when unchecked. The AI cannot call what it cannot see. No reliance on the model following natural-language instructions.

**Post-filter by domain, not query-level site: restriction** — Appending `site:x.com OR site:y.com` to the user's query is fragile and pollutes search intent. Post-filtering in `SearXNGClient` is clean, configurable, and testable in isolation.

**Return empty when filter yields no results** — No unfiltered fallback. Returning off-topic results would degrade answer quality more than returning nothing.

**Separate `SearchWebTool.cs`** — Keeps `DndMcpTools.cs` focused on RAG tools. Each tool class has one responsibility.

**Plain `HttpClient` via `IHttpClientFactory`** — SearXNG has no .NET SDK. The API is a single GET endpoint; a named `HttpClient` registered in DI is sufficient.

**`DndChatService.SendAsync` takes `bool allowWebSearch`** — `Chat.razor` owns the checkbox state and passes it as a primitive. The service filters internally by tool name. No DI changes needed.

## Components

### `Features/Search/SearXNGOptions.cs`
Record bound from `"SearXNG"` config section:

- `Url` — base URL of the SearXNG instance (default `http://searxng:8080`)
- `MaxResults` — max results to request (default 5)
- `AllowedDomains` — string array of domain substrings to filter by (e.g. `["dndbeyond.com", "5etools.com", "reddit.com", "enworld.org", "roll20.net", "sageadvice.eu"]`)

### `Features/Search/SearXNGResult.cs`
Record with `Title`, `Url`, `Snippet` mapped from SearXNG JSON (`results[].title`, `.url`, `.content`).

### `Features/Search/SearXNGClient.cs`
Typed `HttpClient`. `SearchAsync(string query, CancellationToken ct)`:

1. GET `/search?q=<query>&format=json&language=en`
2. Deserialize `results` array
3. Filter: keep entries where `Url` contains any `AllowedDomains` entry (case-insensitive)
4. Return up to `MaxResults` entries; return empty list on network/parse failure (log warning)

### `Features/Search/SearchWebTool.cs`
`[McpServerToolType]` class, single `[McpServerTool]` method `search_web(string query)`:

- Description: *"Search the live web for D&D rules, lore, or community discussions not found in local books. Only call this when the user explicitly asks to search the web."*
- Calls `SearXNGClient.SearchAsync`
- Returns JSON array of `{ title, url, snippet }` or `"No web results found."` if empty

### `DndMcpAICompanion/Features/Chat/DndChatService.cs`
`SendAsync(string userMessage, bool allowWebSearch, CancellationToken ct)`:

- Builds active tools: `allowWebSearch ? tools : tools.Where(t => t.Metadata?.Name != "search_web")`
- Passes filtered list to `ChatOptions.Tools`

### `DndMcpAICompanion/Components/Pages/Chat.razor`

- `bool _webSearchEnabled` field, default `false`
- Checkbox rendered near the send button, label "Search web"
- Passes `_webSearchEnabled` to `SendAsync`

### `infra/searxng/settings.yml`
Minimal SearXNG config:

- `server.secret_key`: fixed local string
- `server.limiter`: `false`
- `search.formats`: `[html, json]`

### `docker-compose.yml`
New `searxng` service:

- Image: `searxng/searxng:latest`
- Port: `8888:8080` (host:container)
- Network: `dnd_net`
- Volume: `./infra/searxng/settings.yml:/etc/searxng/settings.yml:ro`

### `Config/appsettings.json`
```json
"SearXNG": {
  "Url": "http://searxng:8080",
  "MaxResults": 5,
  "AllowedDomains": ["dndbeyond.com", "5etools.com", "reddit.com", "enworld.org", "roll20.net", "sageadvice.eu"]
}
```

## Error Handling

- SearXNG unreachable or non-2xx → `SearXNGClient` returns `[]`, logs warning at `Warning` level, no throw
- JSON parse failure → same: return `[]`, log warning
- Domain filter yields zero results → return `[]` (no fallback to unfiltered)
- `search_web` returns `"No web results found."` when client returns empty

## Testing

- `SearXNGClientTests`: domain filter keeps matching URLs, rejects non-matching, returns empty on HTTP failure
- `DndChatServiceTests`: `search_web` absent from tool list when `allowWebSearch=false`; present when `true`
