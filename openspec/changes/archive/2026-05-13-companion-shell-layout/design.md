## Context

`MainLayout.razor` is a single `<main>@Body</main>` — no navigation, no auth state, no logout. Login/Register pages have CSS class names (`auth-container`, `form-group`, `error`) but no matching styles in `app.css`. Heroes are currently only reachable by navigating into a campaign first.

## Goals / Non-Goals

**Goals:**

- Left sidebar shell visible on all authenticated pages: app name, Chat / Campaigns / Heroes links, username, logout
- `AuthLayout.razor` — centered card layout for login/register with no sidebar
- `/heroes` page listing all heroes across the user's campaigns, linking to each hero's campaign-scoped detail URL
- `HeroRepository.GetAllByUserAsync(userId)` to support the heroes page without N+1 queries
- Auth form CSS and sidebar CSS added to `app.css`

**Non-Goals:**

- Mobile-responsive collapsible sidebar (out of scope)
- Hero creation from the heroes page (heroes are created inside campaigns)
- Changing any existing page routing or business logic

## Decisions

### Two separate layouts: MainLayout + AuthLayout

`Login.razor` and `Register.razor` declare `@layout AuthLayout` to opt into the stripped-down centered card. All other pages use `MainLayout` via the router default. This is the standard Blazor pattern and avoids conditional auth-state rendering inside a single layout.

**Alternative considered:** Single layout with conditional sidebar based on auth state — rejected because it requires `AuthorizeView` in the layout, complicates CSS, and makes the sidebar flash before auth state resolves.

### Username from `AuthenticationStateProvider` in `MainLayout`

`MainLayout` injects `AuthenticationStateProvider` and reads `ClaimTypes.Name` to display the username. This is already available via the cascading `CascadingAuthenticationState` in `Routes.razor`.

### Heroes page queries via `GetAllByUserAsync`

A single SQL query JOINs `Heroes` → `Campaigns` filtering by `userId`. This returns all heroes in one round-trip. The heroes page shows them grouped by campaign name with a link to `/campaigns/{campaignId}/heroes/{heroId}`.

**Alternative considered:** Load all campaigns then call `GetByCampaignAsync` for each — rejected due to N+1 queries.

### CSS in `app.css`

Sidebar and auth styles are appended to the existing `app.css`. No new CSS files or CSS isolation (`.razor.css`) because the project currently has a single flat CSS file and component isolation would be inconsistent.

## Risks / Trade-offs

- **Risk**: Sidebar grows cluttered as more pages are added.  

  **Mitigation**: Three links is fine for now; collapsing or grouping can be added later.

- **Risk**: Auth state flash — sidebar might briefly appear before redirecting unauthenticated users.  

  **Mitigation**: `AuthorizeRouteView` in `Routes.razor` handles redirects; `MainLayout` only renders sidebar content, not auth guards.
