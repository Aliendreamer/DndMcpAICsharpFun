# mcp-server Specification

## Purpose
TBD - created by archiving change mcp-retrieval-tools. Update Purpose after archive.
## Requirements
### Requirement: MCP endpoint is available at /mcp
The server SHALL expose a Streamable HTTP MCP endpoint at `/mcp` using the `ModelContextProtocol.AspNetCore` SDK.

#### Scenario: MCP endpoint responds to initialize

- **WHEN** a client sends a valid MCP `initialize` request to `/mcp`
- **THEN** the server responds with its capabilities and the list of available tools

### Requirement: MCP endpoint requires X-Mcp-Api-Key authentication
The server SHALL reject requests to `/mcp` that do not include a valid `X-Mcp-Api-Key` header.

#### Scenario: Request without key is rejected

- **WHEN** a client sends a request to `/mcp` without the `X-Mcp-Api-Key` header
- **THEN** the server returns HTTP 401

#### Scenario: Request with wrong key is rejected

- **WHEN** a client sends a request to `/mcp` with an incorrect `X-Mcp-Api-Key` value
- **THEN** the server returns HTTP 401

#### Scenario: Request with correct key is accepted

- **WHEN** a client sends a request to `/mcp` with the correct `X-Mcp-Api-Key` value
- **THEN** the request is forwarded to the MCP handler

### Requirement: MCP key is configured separately from the admin key
The server SHALL read the MCP API key from `Mcp:ApiKey` in configuration, independent of `Admin:ApiKey`.

#### Scenario: Dev key is configured in Development environment

- **WHEN** the server starts in the Development environment
- **THEN** `Mcp:ApiKey` is set to a non-empty dev value in `appsettings.Development.json`

### Requirement: New tool files are discovered automatically
The server SHALL use `WithToolsFromAssembly()` so any class decorated with `[McpServerTool]` in the assembly is registered without manual wiring.

#### Scenario: Adding a new tool file requires no Program.cs change

- **WHEN** a new file with `[McpServerTool]` methods is added to `Features/Mcp/`
- **THEN** those tools appear in the MCP tools list with no other code changes

