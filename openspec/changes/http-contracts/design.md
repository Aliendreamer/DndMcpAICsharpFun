## Context

The project exposes 8 HTTP endpoints across three feature areas (health, admin books, retrieval) plus a conditional metrics endpoint. There is no `.http` file and no enforced convention to keep endpoint documentation in sync with code. Developers must read the source or README to discover endpoints, and there is no runnable example for each route.

## Goals / Non-Goals

**Goals:**
- Create `DndMcpAICsharpFun.http` as the single runnable reference for all endpoints
- Add a CLAUDE.md rule making `.http` file updates mandatory when routes change
- Cover all 8 current routes with realistic example requests

**Non-Goals:**
- No test or CI check to enforce sync mechanically
- No per-environment variable files (`.env`, `http-client.env.json`)
- No generated or scripted `.http` output — the file is hand-maintained
- No changes to any source, test, config, or infrastructure file

## Decisions

**Single file at project root**
Alternatives: `http/` folder with per-feature files, inline next to each feature's endpoint file. Single root file wins for discoverability (IDE picks it up automatically) and simplicity (8 routes fit comfortably in one file with section headers).

**`@variable` syntax for base URL and API key**
The file uses `@baseUrl = http://localhost:5101` and `@adminKey = YOUR_KEY_HERE` at the top. This is the lowest-friction approach — no separate env file required, works in VS Code REST Client, JetBrains HTTP Client, and `httpyac`. The `YOUR_KEY_HERE` placeholder makes the intent obvious without a `.gitignore` entry.

**Convention via CLAUDE.md, not a test**
A test that parses `.http` files to verify route coverage adds fragile parsing logic with minimal payoff on an 8-route API. The CLAUDE.md rule is the right enforcement lever: Claude Code reads it every session and applies it automatically to endpoint tasks.

## Risks / Trade-offs

[Risk] Developer adds a route without updating `.http` → Mitigation: CLAUDE.md rule makes it a project norm; any PR that omits the update can be caught in review.

[Risk] `@adminKey = YOUR_KEY_HERE` placeholder could be replaced with a real key and accidentally committed → Mitigation: The placeholder string `YOUR_KEY_HERE` is memorable and unlikely to be a real key; adding `*.http` to a future `.gitignore` note is an option if this becomes a concern.
