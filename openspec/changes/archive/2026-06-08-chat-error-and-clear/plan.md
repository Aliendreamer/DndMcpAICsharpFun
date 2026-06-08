# Chat Error Handling & Clear Conversation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface chat send-failures as a dismissible banner (instead of an in-thread "AI unavailable" bubble) and add a confirmed, server-deleting "Clear conversation" control.

**Architecture:** `DndChatService.SendAsync` currently swallows exceptions and appends an assistant error message. Change it to return a `bool` success signal and stop injecting the error bubble; `Chat.razor` renders a banner when it returns `false`. Add `ChatRepository.DeleteConversationAsync` (scoped, `ExecuteDeleteAsync`) and `DndChatService.ClearAsync`, wired to a confirm-guarded button.

**Tech Stack:** Blazor Server, EF Core (Npgsql), xUnit + FluentAssertions + Testcontainers/Respawn (`[Collection("postgres")]`), Microsoft.Extensions.AI.

---

## File Structure

- `Features/Chat/ChatRepository.cs` — add `DeleteConversationAsync`
- `Features/Chat/DndChatService.cs` — `SendAsync` returns `bool`; add `ClearAsync`
- `Components/Pages/Chat.razor` — error banner state + clear button + confirm dialog
- `DndMcpAICsharpFun.Tests/Chat/ChatRepositoryTests.cs` — delete test
- `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` — update 2 tests, add 1

---

### Task 1: ChatRepository.DeleteConversationAsync (TDD)

**Files:**
- Modify: `Features/Chat/ChatRepository.cs`
- Test: `DndMcpAICsharpFun.Tests/Chat/ChatRepositoryTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `ChatRepositoryTests`:

```csharp
[Fact]
public async Task DeleteConversation_removes_only_the_users_scoped_turns()
{
    await _repo.AddAsync(new ChatTurn { UserId = 1, Role = "user", Content = "a", CreatedAt = DateTime.UtcNow });
    await _repo.AddAsync(new ChatTurn { UserId = 1, Role = "assistant", Content = "b", CreatedAt = DateTime.UtcNow.AddSeconds(1) });
    await _repo.AddAsync(new ChatTurn { UserId = 2, Role = "user", Content = "theirs", CreatedAt = DateTime.UtcNow });
    await _repo.AddAsync(new ChatTurn { UserId = 1, CampaignId = 5, Role = "user", Content = "scoped", CreatedAt = DateTime.UtcNow });

    await _repo.DeleteConversationAsync(1);

    (await _repo.GetHistoryAsync(1)).Should().BeEmpty();
    (await _repo.GetHistoryAsync(2)).Should().ContainSingle(t => t.Content == "theirs");
    (await _repo.GetHistoryAsync(1, campaignId: 5)).Should().ContainSingle(t => t.Content == "scoped");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ChatRepositoryTests.DeleteConversation_removes_only_the_users_scoped_turns"`
Expected: FAIL — `DeleteConversationAsync` does not exist (compile error).

- [ ] **Step 3: Implement the method**

Add to `ChatRepository` (after `GetHistoryAsync`):

```csharp
public async Task DeleteConversationAsync(long userId, long? campaignId = null, long? heroId = null)
{
    await using var db = await dbf.CreateDbContextAsync();
    await db.ChatTurns
        .Where(m => m.UserId == userId && m.CampaignId == campaignId && m.HeroId == heroId)
        .ExecuteDeleteAsync();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ChatRepositoryTests.DeleteConversation_removes_only_the_users_scoped_turns"`
Expected: PASS (Docker must be running for the Postgres fixture).

- [ ] **Step 5: Commit**

```bash
git add Features/Chat/ChatRepository.cs DndMcpAICsharpFun.Tests/Chat/ChatRepositoryTests.cs
git commit -m "feat(chat): scoped DeleteConversationAsync on ChatRepository"
```

---

### Task 2: DndChatService — signal failure + ClearAsync (TDD)

**Files:**
- Modify: `Features/Chat/DndChatService.cs`
- Test: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs`

- [ ] **Step 1: Update the existing tests to the new contract**

In `DndChatServiceTests`, replace the two return-value assertions:

`SendAsync_appends_user_and_assistant_messages_to_history` — change the first assertion line from
`reply.Should().Be("Fireball deals 8d6 fire damage.");` to:

```csharp
var ok = await svc.SendAsync("What does fireball do?", false, CancellationToken.None);

ok.Should().BeTrue();
svc.History.Should().HaveCount(2);
```
(keep the remaining `History[...]` assertions; remove the old `var reply = ...` line.)

Replace `SendAsync_returns_error_message_when_ollama_is_unreachable` entirely with:

```csharp
[Fact]
public async Task SendAsync_returns_false_and_adds_no_assistant_message_when_unreachable()
{
    var client = new FakeChatClient { ShouldThrow = true };
    var svc = CreateService(client);

    var ok = await svc.SendAsync("Hello", false, CancellationToken.None);

    ok.Should().BeFalse();
    svc.History.Should().ContainSingle();          // user message only
    svc.History[0].Role.Should().Be(ChatRole.User);
}
```

The two tool-filtering tests call `await svc.SendAsync(...)` ignoring the return value — leave them unchanged.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: FAIL — `SendAsync` returns `string`, not `bool` (compile error / type mismatch).

- [ ] **Step 3: Change `SendAsync` to return `bool`**

In `DndChatService.SendAsync`, change the signature to `public async Task<bool> SendAsync(...)` and update the bodies:
- rate-limited branch: after adding the two messages, `return true;`
- success branch: after `await PersistAsync("assistant", reply);`, `return true;`
- `catch (OperationCanceledException) { throw; }` — unchanged
- `catch (Exception)` branch: replace its body with `return false;` (do NOT add an assistant message)

Resulting catch section:

```csharp
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
```

- [ ] **Step 4: Add `ClearAsync`**

Add after `SendAsync` (above `PersistAsync`):

```csharp
public async Task ClearAsync()
{
    History.Clear();
    var idClaim = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (long.TryParse(idClaim, out var userId))
        await chatRepository.DeleteConversationAsync(userId);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Features/Chat/DndChatService.cs DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs
git commit -m "feat(chat): SendAsync signals failure; add ClearAsync"
```

---

### Task 3: Chat.razor — banner, clear button, confirm

**Files:**
- Modify: `Components/Pages/Chat.razor`

- [ ] **Step 1: Add error state + clear it on send**

In `@code`, add field `private string? _sendError;`. In `Send()`, set `_sendError = null;` immediately after the guard, and capture the result:

```csharp
    private async Task Send()
    {
        var msg = _input.Trim();
        if (string.IsNullOrEmpty(msg) || _loading) return;

        _sendError = null;
        _input = "";
        _loading = true;
        _scrollRequired = true;
        StateHasChanged();

        try
        {
            var ok = await ChatService.SendAsync(msg, _webSearchEnabled, _cts.Token);
            if (!ok) _sendError = "Couldn't send message. Please try again.";
        }
        finally
        {
            _loading = false;
        }
    }
```

- [ ] **Step 2: Render the dismissible banner**

Directly inside `<div class="chat-container">`, above `<div class="messages" ...>`:

```razor
    @if (_sendError is not null)
    {
        <div class="chat-error-banner" role="alert">
            <span>⚠ @_sendError</span>
            <button class="banner-dismiss" @onclick="() => _sendError = null">✕</button>
        </div>
    }
```

- [ ] **Step 3: Add the Clear conversation button + handler**

Add a button in the `input-row` (after Send) — or a small header control; place after the Send button:

```razor
        <button class="clear-conversation" @onclick="ClearConversation" disabled="@_loading" title="Clear conversation">Clear</button>
```

Add the handler in `@code`:

```csharp
    private async Task ClearConversation()
    {
        var confirmed = await JS.InvokeAsync<bool>(
            "confirm", "Clear conversation? This permanently deletes all messages.");
        if (!confirmed) return;

        await ChatService.ClearAsync();
        _sendError = null;
        ChatService.History.Add(new ChatMessage(
            ChatRole.Assistant,
            "Hello, adventurer! Ask me anything about D&D rules, spells, monsters, and lore."));
        _scrollRequired = true;
    }
```

- [ ] **Step 4: Add minimal CSS**

Append to the chat stylesheet (find the existing chat CSS, e.g. `wwwroot/css/*` used by these pages; if styles are inline/co-located, match that pattern):

```css
.chat-error-banner { display:flex; justify-content:space-between; align-items:center;
  background:#3b1f1f; color:#f5b5b5; border:1px solid #6e2b2b; border-radius:6px;
  padding:.5rem .75rem; margin-bottom:.5rem; }
.banner-dismiss { background:none; border:none; color:inherit; cursor:pointer; font-size:1rem; }
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: succeeds with zero warnings (warnings-as-errors).

- [ ] **Step 6: Commit**

```bash
git add Components/Pages/Chat.razor wwwroot
git commit -m "feat(chat): error banner + clear-conversation button"
```

---

### Task 4: Full verification

- [ ] **Step 1: Build + test**

Run: `dotnet build && dotnet test` (Docker running)
Expected: all green, zero warnings.

- [ ] **Step 2: Manual smoke**

With the stack up: stop the LLM (or point `Ollama` at a dead URL) → send a message → red banner appears, no "AI unavailable" bubble, input row usable. Dismiss banner; send a working message → banner gone. Click Clear → confirm → thread resets to greeting; reload page → no history replays.

- [ ] **Step 3: Commit any fixups**

```bash
git add -A && git commit -m "test(chat): verify error-banner and clear flow"
```
