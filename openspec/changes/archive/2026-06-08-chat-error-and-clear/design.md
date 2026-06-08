## Context

`Chat.razor` already has a `_loading` flag, scroll-to-bottom, and disabled-while-busy inputs. Its `Send` path persists each turn via `ChatRepository.AddAsync` and replays history via `GetHistoryAsync`. There is no error feedback on failure and no delete capability on the repository.

## Goals / Non-Goals

**Goals:**
- Surface send failures without losing the user's typed input or thread state.
- Let the user permanently clear the active conversation, server-side included.

**Non-Goals:**
- Per-message retry (a dismissible banner was chosen over inline retry).
- Streaming/token-level rendering.
- Clearing across all conversation scopes at once.

## Decisions

- **Dismissible banner over inline error bubble.** Simpler state model: a single `_sendError` string rendered as a banner above the thread with an `×` to dismiss; cleared automatically at the start of the next successful send. Inline per-message retry was considered but adds message-state bookkeeping for marginal value on a fast-win.
- **Hard delete with confirmation over soft-clear.** "Clear conversation" deletes the user's `ChatTurn` rows so history does not replay after reload. A confirmation dialog guards the destructive action. View-only clearing was rejected because reload would resurrect the thread, which is surprising.
- **Scope-aware delete.** `DeleteConversationAsync(userId, campaignId?, heroId?)` mirrors `GetHistoryAsync`'s scoping so only the active conversation is removed, not every conversation the user owns.

## Risks / Trade-offs

- [Accidental data loss] → Confirmation dialog before delete; delete is scoped to the current conversation only.
- [Banner masks repeated failures] → Banner text is generic; detailed errors stay in server logs (no sensitive data leaked to the UI).
