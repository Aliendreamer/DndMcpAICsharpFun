## Why

The companion app has no navigation shell — `MainLayout.razor` is a single `<main>@Body</main>` wrapper, so users have no way to move between Chat, Campaigns, and Heroes, see who they're logged in as, or log out. Auth pages also have no CSS, making login look unstyled.

## What Changes

- Add `DndMcpAICompanion/Components/Layout/AuthLayout.razor` — minimal centered layout for login/register (no sidebar)
- Rewrite `DndMcpAICompanion/Components/Layout/MainLayout.razor` — left sidebar shell with app name, nav links (Chat, Campaigns, Heroes), username, and logout button
- Add `DndMcpAICompanion/Components/Pages/Heroes.razor` — top-level heroes listing page at `/heroes` showing all heroes across the user's campaigns
- Add `HeroRepository.GetAllByUserAsync(long userId)` — query heroes across all of the user's campaigns
- Update `Login.razor` and `Register.razor` to declare `@layout AuthLayout`
- Add sidebar CSS, auth form CSS, and heroes page CSS to `DndMcpAICompanion/wwwroot/app.css`

## Capabilities

### New Capabilities

- `companion-shell-layout`: Left-sidebar navigation shell for the companion Blazor app — shows app name, Chat / Campaigns / Heroes nav links, current username, and logout; auth pages use a separate centered `AuthLayout`; includes a new top-level `/heroes` page listing all heroes across campaigns

### Modified Capabilities

_(none — no existing spec-level requirements are changing)_

## Impact

- `DndMcpAICompanion/Components/Layout/MainLayout.razor` — rewritten
- `DndMcpAICompanion/Components/Layout/AuthLayout.razor` — new file
- `DndMcpAICompanion/Components/Pages/Login.razor` — adds `@layout AuthLayout`
- `DndMcpAICompanion/Components/Pages/Register.razor` — adds `@layout AuthLayout`
- `DndMcpAICompanion/wwwroot/app.css` — sidebar and auth form styles appended
- No backend changes, no new dependencies
