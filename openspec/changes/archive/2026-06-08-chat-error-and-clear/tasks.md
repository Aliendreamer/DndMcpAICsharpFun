## 1. Repository delete path (TDD)

- [x] 1.1 Write failing `ChatRepository` test: `DeleteConversationAsync(userId, campaignId, heroId)` removes only the matching user's scoped turns and leaves other scopes/users intact (Postgres via Testcontainers)
- [x] 1.2 Implement `ChatRepository.DeleteConversationAsync(long userId, long? campaignId = null, long? heroId = null)` mirroring `GetHistoryAsync` scoping
- [x] 1.3 Tests green

## 2. Chat error banner

- [x] 2.1 Add `_sendError` state to `Chat.razor`; set it in the `Send` catch path and clear it at the start of each send
- [x] 2.2 Render a dismissible banner above the thread when `_sendError` is set, with an `×` dismiss control
- [x] 2.3 Verify the loading indicator clears on failure and typed input/thread are preserved

## 3. Clear conversation

- [x] 3.1 Add a "Clear conversation" button to `Chat.razor` with a confirmation dialog
- [x] 3.2 On confirm, call `DeleteConversationAsync` for the active scope, then empty the in-memory thread
- [x] 3.3 Confirm reload shows no replayed history for the cleared scope

## 4. Verification

- [x] 4.1 `dotnet build` (warnings-as-errors) and `dotnet test` green
- [x] 4.2 Manual: trigger a send failure (stop MCP) → banner; clear a conversation → confirm empty after reload
