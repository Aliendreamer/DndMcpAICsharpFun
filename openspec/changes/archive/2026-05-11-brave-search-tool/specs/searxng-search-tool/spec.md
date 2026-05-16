## ADDED Requirements

### Requirement: search_web MCP tool queries SearXNG
The MCP server SHALL expose a `search_web` tool that accepts a `query` string and returns up to `SearXNG:MaxResults` (default 5) results, each containing `title`, `url`, and `snippet`, by calling the local SearXNG instance at `GET /search?q=<query>&format=json`.

#### Scenario: Successful web search

- **WHEN** the AI calls `search_web` with a non-empty query string
- **THEN** the tool returns a JSON array of results with `title`, `url`, and `snippet` fields sourced from SearXNG

#### Scenario: No results found

- **WHEN** the AI calls `search_web` with a query that yields no SearXNG results
- **THEN** the tool returns an empty array without error

#### Scenario: SearXNG unreachable

- **WHEN** the SearXNG service is not reachable or returns a non-2xx response
- **THEN** the tool returns an empty result and logs a warning; it does not throw an unhandled exception

### Requirement: SearXNG runs as a Docker Compose service
The stack SHALL include a `searxng` service using the official SearXNG Docker image, connected to the `dnd_net` network, so the MCP server can reach it at `http://searxng:8080`.

#### Scenario: SearXNG starts with the stack

- **WHEN** `docker compose up -d` is run
- **THEN** a `searxng` container starts on the `dnd_net` network and is reachable at `http://searxng:8080/search`

#### Scenario: MCP server connects to SearXNG by service name

- **WHEN** the MCP server calls `search_web`
- **THEN** it sends a GET request to `http://searxng:8080/search?q=<query>&format=json`

### Requirement: SearXNG URL is configurable
The MCP server SHALL read the SearXNG base URL from `SearXNG:Url` in configuration, defaulting to `http://searxng:8080`, overridable via the `SearXNG__Url` environment variable.

#### Scenario: Default URL used in Docker Compose

- **WHEN** no `SearXNG__Url` environment variable is set
- **THEN** the tool uses `http://searxng:8080` to reach SearXNG

#### Scenario: URL overridden for local development

- **WHEN** `SearXNG__Url` is set to `http://localhost:8888`
- **THEN** the tool sends requests to that URL instead
