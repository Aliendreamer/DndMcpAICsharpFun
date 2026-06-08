## Context

`MainLayout.razor` renders a sidebar with `NavLink` items for Chat (`/`, `NavLinkMatch.All`), Campaigns (`/campaigns`), and Heroes (`/heroes`), each using `ActiveClass="active"`, plus a logout link. The default `NavLink` prefix match already keeps `/campaigns` active on `/campaigns/{id}`; this needs verifying, and a username display needs adding.

## Goals / Non-Goals

**Goals:**
- Document active-route highlighting.
- Ensure parent highlighting holds on nested detail routes.
- Show the signed-in username in the sidebar.

**Non-Goals:**
- Breadcrumbs.
- Collapsible/responsive sidebar redesign.

## Decisions

- **Lean on `NavLink` prefix matching for nested routes.** `/campaigns` with the default (prefix) match already highlights on `/campaigns/{id}` and hero detail routes nested under it; the task verifies this and only adjusts `Match` if a route falls outside the prefix.
- **Username from auth state.** Read the display name from the authentication state (same source the rest of the UI uses) and render it in the sidebar; no new service.

## Risks / Trade-offs

- [Hero detail route not under /campaigns prefix] → If hero detail is routed outside `/campaigns/...`, add an explicit active rule; verified in tasks.
