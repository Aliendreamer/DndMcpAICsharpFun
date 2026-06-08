# List/Chat Empty States — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lock in friendly empty-state messaging for Campaigns, Heroes, and Chat.

**Architecture:** Mostly verification. Campaigns and Heroes already render empty states; Chat already injects a greeting message when history is empty (`Chat.razor` `OnInitializedAsync`), which serves as the chat empty state. This plan verifies all three against the spec and only changes copy if it diverges.

**Tech Stack:** Blazor Server.

> **Reality note (from code inspection):** `Campaigns.razor` shows "No campaigns yet. Create your first one!"; `Heroes.razor` shows "No heroes yet. Create heroes inside a campaign."; `Chat.razor` adds a greeting bubble when `History.Count == 0`. There is **no new code expected** unless a scenario diverges.

---

## File Structure

- `Components/Pages/Campaigns/Campaigns.razor` — verify empty-state branch
- `Components/Pages/Heroes.razor` — verify empty-state branch
- `Components/Pages/Chat.razor` — verify greeting-as-empty-state; formalize only if desired

---

### Task 1: Verify Campaigns & Heroes empty states

- [ ] **Step 1: Confirm Campaigns branch**

Open `Components/Pages/Campaigns/Campaigns.razor`; confirm the `_campaigns.Count == 0 && !_showCreateForm` branch renders the invite message. Matches spec scenario "No campaigns yet" → no change.

- [ ] **Step 2: Confirm Heroes branch**

Open `Components/Pages/Heroes.razor`; confirm the `_heroes.Count == 0` branch renders the directive message. Matches spec scenario "No heroes yet" → no change.

---

### Task 2: Verify Chat empty state

- [ ] **Step 1: Confirm greeting-as-empty-state**

Open `Components/Pages/Chat.razor`; confirm `OnInitializedAsync` adds the assistant greeting when `History.Count == 0`. This satisfies "Fresh conversation shows an empty-state prompt." The greeting is replaced by real turns as the user chats, satisfying "Empty state clears once a message exists."

- [ ] **Step 2: Decide on formalization (optional)**

If a dedicated styled empty-state block is preferred over the greeting bubble, add (above the `messages` loop) a block shown when `ChatService.History.Count == 0`. **Default: keep the greeting** (YAGNI) and record that decision. If `chat-error-and-clear` is also implemented, its `ClearConversation` re-adds the greeting, keeping this consistent.

---

### Task 3: Verification

- [ ] **Step 1: Build**

Run: `dotnet build`
Expected: succeeds, zero warnings.

- [ ] **Step 2: Manual smoke**

A brand-new account sees: empty Campaigns invite, empty Heroes directive, and a Chat greeting. Each is replaced once content exists.

- [ ] **Step 3: Commit (only if any copy/markup changed)**

```bash
git add -A && git commit -m "chore(ui): align empty-state copy with spec"
```
