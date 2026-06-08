## Why

The chat page silently swallows send failures — when the MCP client or network errors, the user sees the "Thinking…" bubble vanish with no explanation. There is also no way to clear a conversation; persisted `ChatTurn` history replays forever with no user control.

## What Changes

- Add a dismissible error banner above the chat thread that appears when a message fails to send (MCP/network error) and auto-clears on the next successful send.
- Add a "Clear conversation" button that, after a confirmation dialog, permanently deletes the signed-in user's `ChatTurn` rows for the active conversation scope and clears the on-screen thread.
- Add `ChatRepository.DeleteConversationAsync(userId, campaignId?, heroId?)` (no delete path exists today).

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `blazor-chat-ui`: adds requirements for send-failure error surfacing and conversation clearing.

## Impact

- `Components/Pages/Chat.razor` (error banner state, clear button + confirm, error-on-failure handling)
- `Features/Chat/ChatRepository.cs` (new `DeleteConversationAsync`)
- Tests: `ChatRepository` delete behavior (Postgres/Testcontainers)
