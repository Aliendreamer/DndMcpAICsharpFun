# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A D&D-themed ASP.NET Core app on .NET 10.0. A **single host** (port 5101) serves three things at once: the ingestion/RAG **API**, the **MCP server** (`/mcp`, Model Context Protocol tools for AI integration), and the **Blazor Server UI** (auth, campaigns, heroes, chat). The companion UI was merged into this project — there is no separate `DndMcpAICompanion` project.

## Commands

Standard `dotnet build` / `run` / `watch run` / `test` apply (the host serves API + MCP + Blazor UI on <http://localhost:5101>). For the chat-quality eval (`Tools/ModelEval`), use the `model-eval` skill.

## Architecture

- **Program.cs** — composition root; wires the API, MCP server, and the Blazor UI (auth, rate limiting, chat) in one host
- **CompanionUI/Components/** — Blazor Server UI components; **wwwroot/** (repo root) — its static assets (`app.css`, `app.js`), served as static web assets so `dotnet publish` includes them
- **Infrastructure/Persistence/** — `AppDbContext` (EF Core, **PostgreSQL** via Npgsql), the single context for all tables
- **Config/** — `appsettings.json` and `appsettings.Development.json` (loaded automatically by the host)

### Persistence

All relational data lives in one EF Core `AppDbContext` on **PostgreSQL** (Npgsql), configured by a connection string from the `Postgres` config section (`Postgres__*` env overrides; dev/test default to `localhost`, the container to the `postgres` service). Repositories (`UserRepository`, `CampaignRepository`, `HeroRepository`, `ChatRepository`) use `IDbContextFactory<AppDbContext>` for short-lived, Blazor-safe contexts. Schema is created by EF migrations applied at startup (`MigrateDatabaseAsync`); a `test` dev user is seeded. `HeroSnapshot.CharacterSheet` persists as a JSON column. Chat conversation turns are persisted as `ChatTurn` rows keyed by the signed-in user.

Persistence tests run against a **real Postgres** via Testcontainers (`postgres:18-alpine`), isolated per test with Respawn — so **Docker must be running** to run the persistence tests. The non-persistence tests need no database. (There is no SQLite anywhere in the project; the one-time SQLite→Postgres migration was performed by a now-retired `Tools/SqliteToPostgres` console.)

> The chat feature is an MCP **client** that calls this same host's MCP server over a loopback endpoint (`McpClient:Url`), initialised lazily on first use to avoid a single-process startup deadlock. The server's `Mcp:ApiKey` and the client's `McpClient:ApiKey` must match.

### Key project settings

Build configuration is centralized at the repo root — edit there, not in csproj files:

- `Directory.Build.props` — `net10.0`, nullable, implicit usings, **warnings-as-errors** for every project
- `Directory.Packages.props` — ALL NuGet package versions (Central Package Management); csproj `PackageReference`s are version-less
- `Directory.Build.targets` — shared test stack (xunit, FluentAssertions, Test.Sdk, coverlet) for projects with `<IsTestProject>true</IsTestProject>`
- `Build/GenerateCanonicalSchemas.targets` — regenerates `Schemas/canonical/*.schema.json` from domain types via `Tools/SchemaGenerator`; incremental (stamp file `obj/canonical-schemas.stamp`; delete it to force regen, `SkipCanonicalSchemaGen=true` to bypass)

### Structured Entity Extraction

The retrieval pipeline uses a dual-collection setup in Qdrant: `dnd_blocks` holds prose chunks for narrative retrieval, while `dnd_entities` holds typed entity records (Class, Monster, Spell, etc.) for structured lookups. Each entity's `canonicalText` is embedded into `dnd_entities` so structured queries return rule-accurate snippets alongside the parsed fields.

`books/canonical/<book-slug>.json` is the hand-correctable source of truth for structured entities (reviewed in PRs before landing). For adding a book, extraction routes (programmatic vs LLM), sidecar files, and the **always-pass-`fivetoolsSourceKey`** rule, use the `book-ingestion` skill.

## Observability

Observability is provided by the shared **PersonalCommandCenter** infra stack (bring it up first) — Prometheus, Grafana, Tempo, and the OTel collector all live there, not in this repo. Metrics reach Prometheus via the stack's OTel collector. The UIs are exposed through the stack's Traefik router at `*.pcc.localhost`:

| Service | URL | Notes |
| --- | --- | --- |
| Grafana | <http://grafana.pcc.localhost> | Dashboards incl. this app's .NET-runtime, Qdrant, and Ollama boards (provisioned in the infra repo's `harness/grafana/provisioning/dashboards/`) |
| Prometheus | <http://prometheus.pcc.localhost> | Metrics store (scrapes the app via the OTel collector) |
| pgAdmin | <http://pgadmin.pcc.localhost> | Browse/query the Postgres tables (incl. `IngestionRecords`) |
| Qdrant UI | <http://qdrant.pcc.localhost/dashboard> | Vector collection browser |

> **Note:** The `/metrics` endpoint is guarded by the admin API key (SEC-09, `AdminApiKeyMiddleware` covers both `/admin` and `/metrics`) — scrapers must send `X-Admin-Api-Key`. It can be disabled by setting `OpenTelemetry:Enabled: false` in configuration.

## API Contracts

`DndMcpAICsharpFun.http` at the project root is the authoritative runnable reference for all API endpoints.

**Rule:** When you touch any HTTP endpoint (`MapGet`, `MapPost`, `MapPut`, `MapDelete`), update `DndMcpAICsharpFun.http` in the same commit. This covers not only adding, renaming, or removing a route, but also **changing an existing route's contract** — auth/required headers (e.g. `X-Admin-Api-Key`), query-string parameters, or request/response body shape. Every registered route must have a corresponding example request in the file, and that example must reflect the route's current auth and parameters. A backend endpoint change is not done until the `.http` file matches.

**Rule:** `dnd-mcp-api.insomnia.json` at the project root is the Yaak-importable collection (Insomnia v4 format). Keep it in sync with `DndMcpAICsharpFun.http` — any change to the `.http` file must be reflected in the `.insomnia.json` file in the same commit. Import into Yaak via **File → Import**.

## Markdown docs

Hand-authored markdown (`README.md`, `CLAUDE.md`, `docs/`) must pass markdownlint with **zero errors**.

**Rule:** Whenever you write or edit one of those markdown files, lint it before committing — run the `/lint-md` skill, or directly `pnpm lint:md:fix` (auto-fix) then `pnpm lint:md` (must report `0 error(s)`). This is part of the dev flow: a markdown change is not done until it lints clean. Config is `.markdownlint-cli2.jsonc` (openspec specs are excluded — `opsx` manages their format). The toolchain is **pnpm** (`packageManager` pins the version); `markdownlint-cli2` is a pinned dev dependency installed to the git-ignored `node_modules/` — run `pnpm install` once if it's absent.
