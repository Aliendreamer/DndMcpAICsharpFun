## Why

The D&D companion AI currently answers only from ingested books and the local knowledge base. When a user asks about something not yet ingested — recent errata, community rulings, third-party content, or general D&D questions — the model either hallucinates or says it doesn't know. A web search tool gives the AI a fallback to retrieve live, public information when the local RAG pipeline has no relevant results.

SearXNG is a self-hosted, open-source metasearch engine — no API key, no quota, no cost. It runs as an additional Docker Compose service alongside the existing stack.

**Deferred — future enhancement.** This capability is designed but not scheduled for implementation.

## What Changes

- Add a `searxng` service to `docker-compose.yml` (official SearXNG Docker image)
- Add a `search_web` MCP tool to `DndMcpAICsharpFun` that queries the local SearXNG instance
- The tool accepts a query string and returns the top N results (title, URL, snippet)
- The companion's AI client picks it up automatically at startup via `ListToolsAsync()` — no companion changes needed
- SearXNG URL stored in `appsettings.json` under `SearXNG:Url`, defaulting to `http://searxng:8080`

## Capabilities

### New Capabilities

- `searxng-search-tool`: MCP tool `search_web` that queries a local SearXNG instance and returns structured results the AI can cite in its reply

### Modified Capabilities

- `mcp-server`: New tool registered alongside `search_lore`, `search_entities`, `get_entity`

## Impact

- **New service**: `searxng` container added to Docker Compose on the `dnd_net` network
- **New dependency**: plain `HttpClient` against the SearXNG JSON API (`/search?q=...&format=json`) — no NuGet package needed
- **Config**: `SearXNG:Url` in `appsettings.json`; no API key required
- **No breaking changes**: existing tools and companion are unaffected
