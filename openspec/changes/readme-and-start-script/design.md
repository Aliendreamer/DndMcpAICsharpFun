## Context

The project has a working ingestion pipeline, RAG retrieval, admin API, and full observability stack. There is no README and no way to start the stack without manually constructing a `docker compose` command with the right environment variable. The `docker-compose.yml` also has a hardcoded `ADMIN_API_KEY` env var reference that is not needed because the key lives in git-crypt-encrypted config files loaded automatically by the ASP.NET Core host.

## Goals / Non-Goals

**Goals:**
- Write a README that fully onboards a new developer: project purpose, architecture, prerequisites with versions, Claude Code plugin setup, running locally, API reference, observability
- Add `start.sh` that encapsulates the correct `docker compose` invocation and validates the environment argument
- Clean up `docker-compose.yml` to remove the unnecessary `Admin__ApiKey` env var and make `ASPNETCORE_ENVIRONMENT` dynamic

**Non-Goals:**
- No CI/CD documentation (not yet in place)
- No secrets or API keys in the README
- No MCP server documentation (not yet built)

## Decisions

**`start.sh` validates `Development` | `Production` explicitly**
Alternatives considered: accept any string (too permissive — wrong values silently produce wrong behavior), use flags like `--env` (more typing, less discoverable for a two-value switch). Explicit positional arg with validation is the simplest correct choice.

**`ASPNETCORE_ENVIRONMENT` sourced from shell in docker-compose, not hardcoded**
The script exports the variable; docker compose interpolates it via `${ASPNETCORE_ENVIRONMENT}`. No `.env` file needed — this is a .NET project with encrypted config, not a JS project.

**`Admin__ApiKey` removed from docker-compose**
The key is configured in `Config/appsettings.Production.json` (git-crypt encrypted). Passing it via env var was redundant and created a false dependency on an external env var that the script does not supply.

**README structure: single file**
Splitting into `docs/` fragments adds navigation overhead with no benefit at this project size. One comprehensive `README.md` is the right call.

## Risks / Trade-offs

`start.sh` has no `-d` toggle — it always runs detached. This is intentional for a launcher script; developers wanting foreground output can run `docker compose up` directly. Risk: low, clearly documented.

`Admin__ApiKey` removal assumes git-crypt is unlocked before running the stack. If a developer skips `git-crypt unlock`, the app will start with a blank admin key and throw at startup. Mitigation: documented prominently in the README's "Running Locally" section.
