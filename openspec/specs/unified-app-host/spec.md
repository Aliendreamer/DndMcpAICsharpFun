# unified-app-host Specification

## Purpose
TBD - created by archiving change merge-companion-into-main. Update Purpose after archive.
## Requirements
### Requirement: A single host serves the API, MCP server, and Blazor UI
The system SHALL run as one ASP.NET Core host that simultaneously exposes the ingestion/RAG/admin API, the MCP server endpoint (`/mcp`), and the Blazor companion UI, on a single port (5101). There SHALL be exactly one deployable application project; the `DndMcpAICompanion` project SHALL NOT exist.

#### Scenario: UI and API served from one host
- **WHEN** the application is running
- **THEN** Blazor UI routes (e.g. login, campaigns, heroes, chat) and API routes (e.g. `/admin/...`, `/retrieval/...`, `/mcp`) are all served by the same process on port 5101

#### Scenario: Companion project removed
- **WHEN** the repository is built
- **THEN** no `DndMcpAICompanion` or `DndMcpAICompanion.Tests` project is referenced or compiled, and the main project no longer excludes `DndMcpAICompanion/**`

### Requirement: Cookie authentication for the UI coexists with API-key and MCP-key auth
The system SHALL authenticate Blazor UI users via cookie authentication while continuing to guard `/admin` routes with the admin API key and `/mcp` with the MCP key. The auth schemes SHALL be path-scoped and SHALL NOT interfere with one another.

#### Scenario: UI requires a signed-in user
- **WHEN** an unauthenticated user requests a protected UI page
- **THEN** they are redirected to the login page

#### Scenario: Admin API still requires the API key
- **WHEN** a request hits an `/admin` route without a valid `X-Admin-Api-Key`
- **THEN** the request is rejected regardless of any UI cookie state

#### Scenario: MCP endpoint still requires the MCP key
- **WHEN** a request hits `/mcp` without a valid MCP key
- **THEN** the request is rejected by the MCP key middleware

### Requirement: Behavior parity with the pre-merge companion
The merged host SHALL preserve the companion's existing pages, routes, and chat behavior. No UI route or page SHALL be removed or renamed by this change.

#### Scenario: Existing UI flows continue to work
- **WHEN** a user logs in, opens a campaign, views a hero, and uses chat
- **THEN** each flow behaves as it did in the standalone companion

