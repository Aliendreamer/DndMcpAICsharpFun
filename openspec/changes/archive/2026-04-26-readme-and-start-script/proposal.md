## Why

New contributors and colleagues have no entry point to understand the project, run it locally, or configure their development environment. The repository has an empty README and no automation for starting the stack.

## What Changes

- Replace the empty `README.md` with a comprehensive developer guide covering overview, architecture, prerequisites, Claude Code setup, running locally, API reference, and observability URLs
- Add `start.sh` — a bash script that accepts `Development` or `Production` and runs `docker compose up --build -d` with the correct `ASPNETCORE_ENVIRONMENT`
- Update `docker-compose.yml` to source `ASPNETCORE_ENVIRONMENT` dynamically from the environment and remove the now-unnecessary `Admin__ApiKey` env var (key lives in encrypted config files)

## Capabilities

### New Capabilities

- `developer-onboarding`: Full README covering project overview, architecture, prerequisites, Claude Code plugin setup (superpowers, serena, csharp-lsp, dotnet-agent-skills, openspec CLI), git-crypt unlock, API endpoints, and observability URLs
- `stack-launcher`: `start.sh` bash script that validates and exports `ASPNETCORE_ENVIRONMENT`, then runs `docker compose up --build -d`

### Modified Capabilities

- `docker-stack`: `docker-compose.yml` updated — `ASPNETCORE_ENVIRONMENT` sourced from shell environment variable; `Admin__ApiKey` env var removed from `app` service

## Impact

- `README.md` — full rewrite
- `start.sh` — new file at project root
- `docker-compose.yml` — two-line change to `app` service environment block
