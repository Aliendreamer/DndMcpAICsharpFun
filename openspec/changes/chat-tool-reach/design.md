## Context

See the proposal for the traced causes.

## Goals / Non-Goals

Goal: the chat reaches the right tool and never answers with nothing. Non-goals: 2024 editions, ranking exactly named entities first in `search_dnd`, a larger question set (all on the roadmap).

## Decisions

- Widen the always-safe core instead of removing the router: 19 tools at once is what the router was made to avoid.
- Retry an empty reply once with every tool: cheap, and it covers any other hidden tool.
- A campaign id that is not the caller's falls back to the books: the model makes ids up, and nothing of a campaign is read.

## Risks / Trade-offs

- A retried turn takes twice as long. It only happens where the user got nothing before.
- `search_lore` stays for external MCP clients with fewer parameters.
