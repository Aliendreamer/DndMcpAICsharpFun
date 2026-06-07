## REMOVED Requirements

### Requirement: Companion Program.cs is reduced to a thin composition root
**Reason**: The `DndMcpAICompanion` project is deleted and merged into the main `DndMcpAICsharpFun` host. There is no longer a separate companion `Program.cs`; its configuration loading, option binding, MCP client initialisation, and extension-method wiring are absorbed into the main program's composition root.

**Migration**: The companion's startup responsibilities are now covered by the `unified-app-host` capability and the main `program-structure` spec. The MCP client initialisation moves to the merged host (lazy, loopback) per the `mcp-client-integration` spec. All other `companion-program-structure` requirements (configuration loading, database/Ollama/MCP-client/auth/rate-limit/Blazor registration, database initialisation, middleware, endpoint mapping delegation) are superseded by the main host's existing extension-method structure; this entire capability is retired.

#### Scenario: Companion program structure no longer exists
- **WHEN** the repository is inspected after the merge
- **THEN** there is no `DndMcpAICompanion/Program.cs`, and the companion-program-structure capability is archived as superseded by `unified-app-host`
