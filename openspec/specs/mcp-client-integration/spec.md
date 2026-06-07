# mcp-client-integration Specification

## Purpose
TBD - created by archiving change blazor-companion. Update Purpose after archive.
## Requirements
### Requirement: MCP client discovers and registers server tools at startup
The companion SHALL connect to the D&D MCP server at startup and make its tools (`search_lore`, `search_entities`, `get_entity`) available to the AI chat client as callable functions.

#### Scenario: Tools are available when the app starts

- **WHEN** the companion application starts
- **THEN** the MCP client has connected to the server and the three tools are registered with the IChatClient

#### Scenario: MCP requests include the API key

- **WHEN** the AI calls any MCP tool
- **THEN** the HTTP request to `/mcp` includes the `X-Mcp-Api-Key` header with the configured key value

### Requirement: AI uses MCP tools to answer D&D questions
The companion's IChatClient SHALL invoke the MCP tools when the user's question requires looking up rules, lore, or entities.

#### Scenario: Lore question triggers search_lore

- **WHEN** a user asks a question about D&D rules or narrative lore
- **THEN** the AI calls the `search_lore` tool and incorporates the results into its reply

#### Scenario: Entity question triggers search_entities

- **WHEN** a user asks about a spell, monster, class, or other D&D entity
- **THEN** the AI calls `search_entities` and uses the returned data to answer

#### Scenario: Follow-up detail triggers get_entity

- **WHEN** the AI needs full details of a specific entity after an initial search
- **THEN** the AI calls `get_entity` with the canonical ID from the search result

### Requirement: MCP connection is configured via appsettings
The companion SHALL read the MCP server URL and API key from configuration, not hardcoded values.

#### Scenario: Dev environment uses localhost

- **WHEN** the app runs in Development environment
- **THEN** `Mcp:Url` points to `http://localhost:5101/mcp` and `Mcp:ApiKey` is set to the dev key

#### Scenario: Container environment uses service name

- **WHEN** the app runs inside Docker Compose
- **THEN** `Mcp:Url` points to `http://app:5101/mcp` using the internal service hostname

