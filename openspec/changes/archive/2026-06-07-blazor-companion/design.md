# D&D Companion — Blazor Client Design

## Goal

A Blazor Server chat companion that lets users ask D&D questions in natural language. The AI uses the MCP tools on the knowledge server to look up rules, lore, spells, and monsters, then answers conversationally. MVP is chat-only; stateful features (session notes, encounter builder, bookmarks) come later.

## Architecture

**New project:** `DndMcpAICompanion/DndMcpAICompanion.csproj`, added to `DndMcpAICsharpFun.slnx`. Runs as a separate Docker Compose service sharing the same internal network as the API server and Ollama.

**MCP connection:** The companion is a pure MCP client — it connects to `http://app:5101/mcp` using the `ModelContextProtocol` client SDK. The MCP server is the only contract between the two projects; no shared assemblies, no direct service references.

**AI layer:** `Microsoft.Extensions.AI` with an Ollama provider (`qwen3:8b`, already running in the stack). The MCP client exposes the server's tools as `AIFunction` objects that `IChatClient` passes to Ollama. Ollama decides when to call them; tool dispatch is handled automatically by the SDK.

**State:** Conversation history is a `List<ChatMessage>` held in a Blazor scoped service (`DndChatService`). Scoped = one instance per browser tab. In-memory only for the MVP.

## Components

### `Services/DndChatService.cs`
Scoped service. Owns `IChatClient`, `IMcpClient`, and `List<ChatMessage> History`. One public method: `Task<string> SendAsync(string userMessage, CancellationToken ct)`. Appends the user message, calls `IChatClient.CompleteAsync` with the MCP tools registered as functions, appends the reply, returns it.

### `Components/Pages/Chat.razor`
The only page (`/`). Full-width messenger layout: scrollable message list above, text input + Send button below. On submit calls `DndChatService.SendAsync`, shows a loading indicator while waiting, appends the response. Conversation resets on page refresh (in-memory scoped service).

### `Program.cs`
Registers:

- `OllamaChatClient` as `IChatClient` (singleton)
- MCP client pointed at the server URL with the API key from config (singleton)
- `DndChatService` as scoped
- Blazor Server interactive components

## Configuration

```json
// appsettings.json
{
  "Ollama": { "Url": "http://ollama:11434", "Model": "qwen3:8b" },
  "Mcp": { "Url": "http://app:5101/mcp", "ApiKey": "" }
}

// appsettings.Development.json
{
  "Ollama": { "Url": "http://localhost:11434", "Model": "qwen3:8b" },
  "Mcp": { "Url": "http://localhost:5101/mcp", "ApiKey": "devMcpKey" }
}
```

A `CompanionOptions` record and `McpClientOptions` record bind these sections. The companion reads `Mcp:ApiKey` and passes it as `X-Mcp-Api-Key` on every request to the MCP server.

## Data Flow

1. User submits message in `Chat.razor`
2. `DndChatService.SendAsync` called — message appended to history
3. `IChatClient.CompleteAsync(history, options)` called with MCP tools attached
4. Ollama reasons, decides to call e.g. `search_entities`
5. MCP client POSTs JSON-RPC to `/mcp` with `X-Mcp-Api-Key` header
6. MCP server executes the tool, returns results
7. Ollama generates reply using tool output, returns it
8. Reply appended to history, `Chat.razor` re-renders

## Error Handling

- Ollama unreachable → catch exception in `DndChatService`, return `"The AI is unavailable right now. Please try again."` as the assistant message
- MCP tool failure → the MCP SDK returns an error result to Ollama; Ollama tells the user it couldn't retrieve that information; no exception surfaces to the UI

## Docker Compose

New service `companion` added to `docker-compose.yml`:

- Image built from `DndMcpAICompanion/Dockerfile`
- Port `5102:8080`
- Depends on `app` and `ollama`
- Same network as the existing services
- `Mcp__ApiKey` injected via environment variable

## Testing

One integration smoke test: start the Blazor app with a fake `IChatClient` (returns a fixed string) and a fake MCP client, load the chat page, verify it renders and the send button is present. No unit tests on Razor components — the logic is too thin to justify it. Verify the golden path manually by running the full stack.

## Out of Scope (MVP)

- Persistent conversation history across page refreshes
- Multiple chat sessions
- Saved entities / bookmarks
- Adventure notes
- Encounter builder
- Authentication
