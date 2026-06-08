## Why

The sidebar already highlights the active top-level route via `NavLink ActiveClass`, but nested detail routes (e.g. `/campaigns/{id}`, hero detail) don't keep their parent item highlighted, and the sidebar doesn't show who is signed in. Both are small, high-value polish items.

## What Changes

- Document the existing active-route highlighting for the top-level Chat / Campaigns / Heroes links.
- Keep the parent nav item (Campaigns) highlighted on nested detail routes.
- Show the signed-in username in the sidebar.

## Capabilities

### New Capabilities
- `sidebar-navigation`: the sidebar's links, active-route highlighting, and signed-in user display.

### Modified Capabilities
<!-- none -->

## Impact

- `Components/Layout/MainLayout.razor` (NavLink match behavior for nested routes; username display)
