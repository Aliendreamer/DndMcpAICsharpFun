# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A D&D-themed ASP.NET Core app on .NET 10.0. A **single host** (port 5101) serves three things at once: the ingestion/RAG **API**, the **MCP server** (`/mcp`, Model Context Protocol tools for AI integration), and the **Blazor Server UI** (auth, campaigns, heroes, chat). The companion UI was merged into this project — there is no separate `DndMcpAICompanion` project.

## Commands

```bash
# Build
dotnet build

# Run (serves API + MCP + Blazor UI on http://localhost:5101)
dotnet run

# Run with hot reload
dotnet watch run

# Restore packages
dotnet restore

# Tests
dotnet test
```

## Architecture

- **Program.cs** — composition root; wires the API, MCP server, and the Blazor UI (auth, rate limiting, chat) in one host
- **Domain/** — all domain model types (`User`, `Campaign`, `Hero`, `HeroSnapshot`, `CharacterSheet`, `ChatTurn`, `IngestionRecord`, entity/book types)
- **Components/**, **wwwroot/** — Blazor Server UI
- **Features/** — `Auth`, `Campaigns`, `Chat`, plus ingestion/retrieval/admin/MCP
- **Infrastructure/Persistence/** — `AppDbContext` (EF Core, SQLite), the single context for all tables
- **Config/** — `appsettings.json` and `appsettings.Development.json` (loaded automatically by the host)

### Persistence

All relational data lives in one EF Core `AppDbContext` (SQLite). Repositories (`UserRepository`, `CampaignRepository`, `HeroRepository`, `ChatRepository`) use `IDbContextFactory<AppDbContext>` for short-lived, Blazor-safe contexts. Schema is created by EF migrations applied at startup (`MigrateDatabaseAsync`); a `test` dev user is seeded. `HeroSnapshot.CharacterSheet` persists as a JSON column. Chat conversation turns are persisted as `ChatTurn` rows keyed by the signed-in user.

> The chat feature is an MCP **client** that calls this same host's MCP server over a loopback endpoint (`McpClient:Url`), initialised lazily on first use to avoid a single-process startup deadlock. The server's `Mcp:ApiKey` and the client's `McpClient:ApiKey` must match.

### Key project settings

Build configuration is centralized at the repo root — edit there, not in csproj files:

- `Directory.Build.props` — `net10.0`, nullable, implicit usings, **warnings-as-errors** for every project
- `Directory.Packages.props` — ALL NuGet package versions (Central Package Management); csproj `PackageReference`s are version-less
- `Directory.Build.targets` — shared test stack (xunit, FluentAssertions, Test.Sdk, coverlet) for projects with `<IsTestProject>true</IsTestProject>`
- `Build/GenerateCanonicalSchemas.targets` — regenerates `Schemas/canonical/*.schema.json` from domain types via `Tools/SchemaGenerator`; incremental (stamp file `obj/canonical-schemas.stamp`; delete it to force regen, `SkipCanonicalSchemaGen=true` to bypass)
- `Microsoft.AspNetCore.OpenApi` is included for OpenAPI/Swagger support

### Configuration

The host listens on `http://localhost:5101` (set in `DndMcpAICsharpFun.http`). Override via `launchSettings.json` or environment variables if needed.

### Structured Entity Extraction (vertical slice)

The retrieval pipeline uses a dual-collection setup in Qdrant: `dnd_blocks` holds prose chunks for narrative retrieval, while `dnd_entities` holds typed entity records (Class, Monster, Spell, etc.) for structured lookups. Each entity's `canonicalText` is embedded into `dnd_entities` so structured queries return rule-accurate snippets alongside the parsed fields.

Canonical JSON files at `books/canonical/<book-slug>.json` are the hand-correctable source of truth for structured entities. Ingestion reads these files via `CanonicalJsonLoader`, validates schema/IDs, and projects each entity into `dnd_entities`. Plan 2 of the structured-entity-extraction effort has shipped: an Ollama-driven extraction pipeline (local qwen3:8b) produces `books/canonical/<book>.json` from Marker conversion output (plus optional sibling `<book>.errors.json` and `<book>.warnings.json`). Hand-authoring is still allowed and remains the source of truth — extraction outputs are reviewed in PRs before they land.

New endpoints:

- `POST /admin/books/{id}/ingest-entities` — ingest a book's canonical JSON into `dnd_entities`.
- `POST /admin/books/{id}/extract-entities` — run Ollama-driven extraction (requires qwen3:8b pulled via ollama-pull); produces `books/canonical/<book-slug>.json` plus optional sibling `<book-slug>.errors.json` and `<book-slug>.warnings.json`. Pass `?force=true` to overwrite an existing canonical JSON. During the run, checkpoint files `<book-slug>.progress.json` and `<book-slug>.progress.errors.json` are written every 100 candidates and deleted on success; a crashed run resumes from the checkpoint on retry.
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
| Marker | <http://localhost:5002/docs> | PDF conversion service Swagger (debugging) |

> **Note:** The `/metrics` endpoint is unauthenticated — rely on Docker network isolation to limit access. It can be disabled by setting `OpenTelemetry:Enabled: false` in configuration.

## API Contracts

`DndMcpAICsharpFun.http` at the project root is the authoritative runnable reference for all API endpoints.

**Rule:** When adding, changing, or removing any HTTP endpoint (`MapGet`, `MapPost`, `MapPut`, `MapDelete`), update `DndMcpAICsharpFun.http` in the same commit. Every registered route must have a corresponding example request in the file.

**Rule:** `dnd-mcp-api.insomnia.json` at the project root is the Yaak-importable collection (Insomnia v4 format). Keep it in sync with `DndMcpAICsharpFun.http` — any change to the `.http` file must be reflected in the `.insomnia.json` file in the same commit. Import into Yaak via **File → Import**.
