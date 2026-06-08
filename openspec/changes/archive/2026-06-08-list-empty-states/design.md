## Context

`Campaigns.razor` renders "No campaigns yet. Create your first one!" when the list is empty and no create form is open; `Heroes.razor` renders "No heroes yet. Create heroes inside a campaign." Both are already shipped. `Chat.razor` has no empty state — a fresh conversation shows an empty thread container.

## Goals / Non-Goals

**Goals:**
- Codify existing list empty states.
- Add a Chat empty state for the no-history case.

**Non-Goals:**
- A shared reusable empty-state component (overkill for three call sites).
- Illustrations/graphics.

## Decisions

- **Document-as-built for Campaigns/Heroes, add one for Chat.** The two list pages are verified against the spec without behavior change; only Chat gets a new block. This honors the "small enhancement" intent without inventing churn where the behavior already exists.
- **Inline empty-state markup over a shared component.** Three short, page-specific messages don't justify a shared abstraction; keep each inline next to its list.

## Risks / Trade-offs

- [Empty-state copy drift] → Low impact; copy is captured in scenarios so it stays intentional.
