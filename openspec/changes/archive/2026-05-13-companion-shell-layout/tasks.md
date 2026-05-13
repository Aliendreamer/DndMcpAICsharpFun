## 1. Backend — HeroRepository

- [x] 1.1 Add `GetAllByUserAsync(long userId)` to `HeroRepository` — single JOIN query: `SELECT h.*, c.Name as CampaignName FROM Heroes h JOIN Campaigns c ON c.Id = h.CampaignId WHERE c.UserId = @uid ORDER BY c.Name, h.Name`; return a list of a new record type `HeroWithCampaign(Hero Hero, string CampaignName)`

## 2. Layout — AuthLayout and MainLayout

- [x] 2.1 Create `DndMcpAICompanion/Components/Layout/AuthLayout.razor` — minimal `@inherits LayoutComponentBase` with a centered `.auth-wrapper > .auth-card` wrapping `@Body`; no sidebar, no nav
- [x] 2.2 Rewrite `DndMcpAICompanion/Components/Layout/MainLayout.razor` — inject `AuthenticationStateProvider`; render `.app-layout` flex container with `.sidebar` (app name, nav links for Chat/Campaigns/Heroes, username + logout at bottom) and `.main-content` wrapping `@Body`; use `NavLink` with `ActiveClass="active"` for highlighted active link
- [x] 2.3 Add `@layout AuthLayout` directive to `Login.razor` (after `@page`)
- [x] 2.4 Add `@layout AuthLayout` directive to `Register.razor` (after `@page`)

## 3. New page — Heroes listing

- [x] 3.1 Create `DndMcpAICompanion/Components/Pages/Heroes.razor` at route `/heroes` — `[Authorize]`, `InteractiveServer`, inject `HeroRepository` and `AuthenticationStateProvider`; on init load `GetAllByUserAsync(userId)`; render a list of heroes showing name, campaign name, level, linked to `/campaigns/{CampaignId}/heroes/{Id}`; show empty-state message when list is empty

## 4. CSS — sidebar, auth forms, heroes page

- [x] 4.1 Append to `app.css`: `.app-layout` (flex row, full height), `.sidebar` (fixed width ~180px, dark bg, flex column, padding), `.sidebar-brand` (purple app name), `.sidebar-nav` (flex column gap, nav links styled), `.sidebar-nav a` and `.sidebar-nav a.active` (colours + active highlight), `.sidebar-footer` (username label + logout button pinned at bottom)
- [x] 4.2 Append to `app.css`: `.auth-wrapper` (full-viewport centered flex), `.auth-card` (dark card, padding, border-radius, max-width ~360px), `.auth-card h2`, `.form-group` (flex column label+input), `.form-group input` (styled dark input), `.error` (red error text), `.auth-card button` (primary button)
- [x] 4.3 Append to `app.css`: `.heroes-page` (padding), `.hero-list-item` (card with name, campaign badge, level, link), `.hero-list-item:hover` highlight

## 5. Verify

- [x] 5.1 Run `dotnet build DndMcpAICompanion` — zero errors
- [x] 5.2 Rebuild Docker image and verify: sidebar visible after login, active link highlights, logout works, `/heroes` lists heroes, login/register pages show styled card
