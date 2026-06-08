## MODIFIED Requirements

### Requirement: MCP connection is configured via appsettings
The application SHALL read the MCP server URL and API key from the `McpClient` configuration section, not hardcoded values and not the `Mcp` section (which is reserved for the MCP **server** options). Because the MCP client and server now run in the same process, the URL SHALL default to a loopback endpoint.

#### Scenario: Dev environment uses localhost loopback

- **WHEN** the app runs in Development environment
- **THEN** `McpClient:Url` points to `http://localhost:5101/mcp` and `McpClient:ApiKey` is set to the dev key

#### Scenario: Container environment uses loopback within the merged service

- **WHEN** the app runs inside Docker Compose
- **THEN** `McpClient:Url` points to the merged service's own `/mcp` endpoint (loopback), supplied via the `McpClient__Url` environment variable, rather than a separate `app` hostname
