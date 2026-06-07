## Why

The D&D MCP knowledge server is built and exposes retrieval tools via MCP, but there is no client to use it. A Blazor Server companion provides a conversational interface where users can ask D&D questions in natural language and get answers backed by the vector-indexed rules, lore, and entity database.

## What Changes

- New project `DndMcpAICompanion` added to the solution — a Blazor Server app with a single chat page
- New Docker Compose service `companion` on port 5102, sharing the existing internal network
- The companion connects to the MCP server via the `ModelContextProtocol` client SDK and uses Ollama (`qwen3:8b`) for AI reasoning and tool dispatch

## Capabilities

### New Capabilities

- `blazor-chat-ui`: Full-width chat page where users send messages and receive AI-generated answers; conversation history held in a scoped service per browser session
- `mcp-client-integration`: MCP client registration that auto-discovers the server's tools (`search_lore`, `search_entities`, `get_entity`) and exposes them to the Ollama `IChatClient` as callable functions

### Modified Capabilities

*(none — no existing requirement changes)*

## Impact

- New project directory: `DndMcpAICompanion/`
- Solution file `DndMcpAICsharpFun.slnx` gains a new project reference
- `docker-compose.yml` gains a `companion` service
- No changes to the existing API server or its tests
