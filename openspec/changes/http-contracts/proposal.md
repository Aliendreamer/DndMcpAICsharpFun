## Why

The project has no HTTP request file, making it tedious to manually craft `curl` commands or remember endpoint signatures. A maintained `.http` file gives every developer (and AI assistant) a runnable reference for every endpoint and encodes the convention that it must stay in sync with route registrations.

## What Changes

- Create `DndMcpAICsharpFun.http` at the project root with example requests for all current endpoints (health, admin books, retrieval, metrics)
- Add an "API Contracts" rule to `CLAUDE.md` requiring `DndMcpAICsharpFun.http` to be updated in the same commit whenever an endpoint is added, changed, or removed

## Capabilities

### New Capabilities

- `http-contracts`: `DndMcpAICsharpFun.http` is the authoritative runnable reference for all API endpoints; kept in sync with route registrations via a CLAUDE.md convention

### Modified Capabilities

- `developer-onboarding`: CLAUDE.md gains an API Contracts section that makes the `.http` file update rule part of the project's working conventions

## Impact

- `DndMcpAICsharpFun.http` — new file at project root
- `CLAUDE.md` — new "API Contracts" section added
- No source, test, or config file changes
