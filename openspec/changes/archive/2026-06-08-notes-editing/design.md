## Context

`CampaignDetail.razor` renders a notes list with add and delete, plus an empty state. `NoteRepository` already exposes `UpdateAsync(id, campaignId, title, content)`. The gap is purely UI: there is no way to invoke the update from the page.

## Goals / Non-Goals

**Goals:**
- Let a user edit an existing note's title and content in place and persist it.

**Non-Goals:**
- Rich text / markdown rendering.
- Optimistic concurrency or multi-user edit conflict handling.

## Decisions

- **Inline edit toggle per note over a separate edit page.** A per-note "Edit" control swaps the card into an editable title input + content textarea reusing the same styling as the add form. Save calls `UpdateAsync`; cancel reverts. This matches the existing add-form interaction and keeps everything on one page.
- **Reuse `UpdateAsync` as-is.** It already scopes by `campaignId`, so no repository change is needed.

## Risks / Trade-offs

- [Concurrent edits overwrite] → Acceptable for a single-user companion; last-write-wins, out of scope to guard.
