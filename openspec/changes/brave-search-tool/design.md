## Context

The MCP server (`DndMcpAICsharpFun`) already exposes `search_lore`, `search_entities`, and `get_entity` tools. Adding `search_web` follows the same pattern: a new tool registered at MCP startup. The companion picks up all tools automatically via `ListToolsAsync()` — no companion changes needed.

SearXNG is a self-hosted metasearch engine that aggregates results from Google, Bing, DuckDuckGo, and others. It exposes a simple JSON REST API with no authentication. Running it as a Docker Compose service keeps the entire stack self-contained and free to operate.

## Goals / Non-Goals

**Goals:**
- Add a `search_web(query)` MCP tool backed by a local SearXNG instance
- SearXNG runs as a Docker Compose service on the `dnd_net` network
- Graceful degradation when SearXNG is unreachable (empty result, no crash)
- Results are structured enough for the AI to cite URL and snippet in its reply

**Non-Goals:**
- Caching or deduplicating web results
- Full-page content fetching (snippets only)
- Any UI changes to the companion
- Configuring SearXNG search engine sources (use SearXNG defaults)

## Decisions

**SearXNG over Brave Search** — Brave Search ended its free tier. SearXNG is free, self-hosted, and aggregates multiple search engines. Running it in Docker Compose is consistent with the rest of the stack.

**Plain `HttpClient` over a NuGet SDK** — SearXNG has no .NET SDK. The API is a single GET endpoint (`/search?q=<query>&format=json`); a typed `HttpClient` registered via `IHttpClientFactory` is sufficient.

**Tool registered on the MCP server, not in the companion** — Keeps all AI tools in one place. The companion is a thin UI layer; tool logic belongs on the server.

**SearXNG always enabled when reachable** — Unlike Brave (which needed an API key to gate the feature), SearXNG requires no credentials. The tool is always registered; if SearXNG is down, the tool returns an empty result gracefully.

**Return top 5 results** — Enough context for the model without flooding the context window. Configurable via `SearXNG:MaxResults` with a default of 5.

## Risks / Trade-offs

- **SearXNG container adds memory overhead** → Small (~100 MB). Acceptable.
- **Noisy web results may degrade answer quality** → The AI may cite incorrect pages. Acceptable as a fallback; local RAG results are always preferred by prompt design.
- **SearXNG rate-limited by upstream search engines** → For personal/low-volume use this is not an issue.

## Open Questions

- Should web results be filtered to D&D-related domains (e.g., `dndbeyond.com`, `5e.tools`) via SearXNG engines config, or left open? Filtering improves relevance but limits utility for general questions.
