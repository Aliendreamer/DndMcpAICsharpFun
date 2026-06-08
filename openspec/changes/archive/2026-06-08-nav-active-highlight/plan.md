# Sidebar Active-Route Highlight & User Display — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Verify the sidebar already highlights the active route (including nested detail routes) and shows the signed-in user.

**Architecture:** Verification only. Code inspection of `MainLayout.razor` shows: `NavLink` items with `ActiveClass="active"` (Chat uses `NavLinkMatch.All`; Campaigns/Heroes use the default **prefix** match), and a `sidebar-username` already bound to `@_username` from `AuthenticationStateProvider`. The default prefix match means `/campaigns` stays active on `/campaigns/{id}` and on the nested hero route `/campaigns/{id}/heroes/{heroId}`.

**Tech Stack:** Blazor Server, `AuthenticationStateProvider`.

> **Reality note:** Both spec enhancements (nested-route highlight, username display) are **already implemented**. Expect **zero code changes** — this plan confirms and locks the behavior.

---

## File Structure

- `Components/Layout/MainLayout.razor` — verify against spec

---

### Task 1: Verify active-route highlighting

- [ ] **Step 1: Top-level + exact match**

Open `Components/Layout/MainLayout.razor`. Confirm Chat (`href="/"`, `NavLinkMatch.All`) is active only on `/`, and Campaigns/Heroes are active on their routes.

- [ ] **Step 2: Nested-route highlighting**

Confirm Campaigns uses the default (prefix) `NavLinkMatch`, so it remains active on `/campaigns/{id}`. Confirm hero detail routes are nested under `/campaigns/...` (route from `CampaignDetail.GoToHero` is `/campaigns/{Id}/heroes/{heroId}`), so Campaigns stays highlighted there too. If any detail route is NOT under the `/campaigns` prefix, add an explicit active rule — otherwise no change.

---

### Task 2: Verify signed-in user display

- [ ] **Step 1: Username binding**

Confirm `OnInitializedAsync` sets `_username` from `state.User.Identity?.Name` and the sidebar footer renders `@_username`. Matches the spec scenario → no change.

---

### Task 3: Verification

- [ ] **Step 1: Build**

Run: `dotnet build`
Expected: succeeds, zero warnings.

- [ ] **Step 2: Manual smoke**

Sign in; navigate Chat / Campaigns / Heroes (each highlights); open a campaign and a hero (Campaigns stays highlighted); confirm the username shows in the sidebar.

- [ ] **Step 3: Commit (only if a gap was found and fixed)**

```bash
git add -A && git commit -m "fix(nav): explicit active rule for nested detail route"
```
