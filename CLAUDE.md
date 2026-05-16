# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A D&D-themed ASP.NET Core Web API on .NET 10.0 intended to expose MCP (Model Context Protocol) tools for AI integration. The project is in early development — `Infrastructure/` is a placeholder for the infrastructure layer and `Program.cs` contains only the minimal host setup.

## Commands

```bash
# Build
dotnet build

# Run (default port: http://localhost:5101)
dotnet run

# Run with hot reload
dotnet watch run

# Restore packages
dotnet restore
```

There are no tests yet. When added, run them with `dotnet test`.

## Architecture

- **Program.cs** — entry point; ASP.NET Core minimal hosting setup
- **Config/** — `appsettings.json` and `appsettings.Development.json` (loaded automatically by the host)
- **Infrastructure/** — intended for infrastructure-layer code (data access, external clients, etc.); currently empty

### Key project settings

- Target framework: `net10.0`
- Nullable reference types: enabled
- Implicit usings: enabled
- `Microsoft.AspNetCore.OpenApi` is included for OpenAPI/Swagger support

### Configuration

The host listens on `http://localhost:5101` (set in `DndMcpAICsharpFun.http`). Override via `launchSettings.json` or environment variables if needed.

### Structured Entity Extraction (vertical slice)

The retrieval pipeline uses a dual-collection setup in Qdrant: `dnd_blocks` holds prose chunks for narrative retrieval, while `dnd_entities` holds typed entity records (Class, Monster, Spell, etc.) for structured lookups. Each entity's `canonicalText` is embedded into `dnd_entities` so structured queries return rule-accurate snippets alongside the parsed fields.

Canonical JSON files at `data/canonical/<book-slug>.json` are the hand-correctable source of truth for structured entities. Ingestion reads these files via `CanonicalJsonLoader`, validates schema/IDs, and projects each entity into `dnd_entities`. Plan 2 of the structured-entity-extraction effort has shipped: an Ollama-driven extraction pipeline (local qwen3:8b) produces `data/canonical/<book>.json` from Docling output (plus optional sibling `<book>.errors.json` and `<book>.warnings.json`). Hand-authoring is still allowed and remains the source of truth — extraction outputs are reviewed in PRs before they land.

New endpoints:

- `POST /admin/books/{id}/ingest-entities` — ingest a book's canonical JSON into `dnd_entities`.
- `POST /admin/books/{id}/extract-entities` — run Ollama-driven extraction (requires qwen3:8b pulled via ollama-pull); produces `data/canonical/<book-slug>.json` plus optional sibling `<book-slug>.errors.json` and `<book-slug>.warnings.json`. Pass `?force=true` to overwrite an existing canonical JSON. During the run, checkpoint files `<book-slug>.progress.json` and `<book-slug>.progress.errors.json` are written every 100 candidates and deleted on success; a crashed run resumes from the checkpoint on retry.
- `POST /admin/canonical/validate` — corpus-wide validation; 200 (clean) / 422 (FAIL-class issues like duplicate IDs across files or schema-version mismatches).
- `GET /retrieval/entities/{id}` — fetch a single entity by ID.
- `GET /retrieval/entities/search` — public typed entity search.
- `GET /admin/retrieval/entities/search` — admin-side entity search with extra fields.

**Adding a new book (Plan 2 onward):**

1. Check `GET /admin/5etools/sources` — if the book appears in the list, note its source key (e.g. `MPMM`).
2. `POST /admin/books/register` — upload PDF. If the book is in 5etools, pass `fivetoolsSourceKey=<KEY>` (e.g. `fivetoolsSourceKey=MPMM`). If not, omit it — the system generates its own IDs from `displayName`. The response always includes `suggestedSources` with fuzzy-matched candidates if you're unsure of the key.
3. `POST /admin/books/{id}/ingest-blocks` — populate `dnd_blocks`.
4. `POST /admin/books/{id}/extract-entities` — produce canonical JSON.
5. Review the canonical JSON diff in a PR; hand-correct any LLM mistakes.
6. `POST /admin/canonical/validate` — pre-merge sanity check.
7. Merge.
8. `POST /admin/books/{id}/ingest-entities` — populate `dnd_entities`.

**5etools source key rule:** Always supply `fivetoolsSourceKey` for any official WotC book. This aligns entity IDs with the 5etools slug (e.g. `mpmm.monster.yuan-ti-anathema`), derives the correct edition from the registry's `publishedYear`, and ensures `POST /admin/5etools/import` can later supplement the same entities with 5etools-sourced data (traitTags → keywords, SRD flags, etc.). For homebrew or third-party books not covered by 5etools, omit the key — the system creates opaque IDs from the display name and that's fine.

## Observability

When the full stack is running (`docker compose up`), these UIs are available:

| Service | URL | Notes |
| --- | --- | --- |
| Grafana | <http://localhost:3000> | Pre-provisioned dashboards for .NET, Qdrant, Ollama |
| Prometheus | <http://localhost:9090> | Metrics scraping and querying |
| sqlite-web | <http://localhost:8080> | Browse `IngestionRecords` table |
| Qdrant UI | <http://localhost:6333/dashboard> | Vector collection browser |

> **Note:** The `/metrics` endpoint is unauthenticated — rely on Docker network isolation to limit access. It can be disabled by setting `OpenTelemetry:Enabled: false` in configuration.

## API Contracts

`DndMcpAICsharpFun.http` at the project root is the authoritative runnable reference for all API endpoints.

**Rule:** When adding, changing, or removing any HTTP endpoint (`MapGet`, `MapPost`, `MapPut`, `MapDelete`), update `DndMcpAICsharpFun.http` in the same commit. Every registered route must have a corresponding example request in the file.

**Rule:** `dnd-mcp-api.insomnia.json` at the project root is the Yaak-importable collection (Insomnia v4 format). Keep it in sync with `DndMcpAICsharpFun.http` — any change to the `.http` file must be reflected in the `.insomnia.json` file in the same commit. Import into Yaak via **File → Import**.
