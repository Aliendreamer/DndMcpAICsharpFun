# DndMcpAICsharpFun

A D&D-themed ASP.NET Core Web API on .NET 10 that ingests D&D rulebook PDFs, embeds their content via [Ollama](https://ollama.ai) into a [Qdrant](https://qdrant.tech) vector store, and exposes semantic search over HTTP — enabling AI assistants to query D&D rules, spells, monsters, and more via RAG (Retrieval-Augmented Generation).

## Architecture

```text
┌──────────────────────────────────────────────────────────────┐
│                      Docker Compose Stack                    │
│                                                              │
│   ┌───────────────────────────────────────────────────── ┐   │
│   │              ASP.NET Core app  :5101                 │   │
│   │                                                      │   │
│   │  Admin API          Ingestion Pipeline   Retrieval   │   │
│   │  /admin/books  →  Background Service  →  /retrieval  │   │
│   │  (register,        (PDF extract,        /search      │   │
│   │   list, reingest)   chunk, embed)                    │   │
│   └────────────────┬────────────┬────────────────────────┘   │
│                    │            │                            │
│              ┌─────▼──┐  ┌─────▼──┐  ┌────────┐              │
│              │ Qdrant  │  │ Ollama  │  │ SQLite │            │
│              │ :6333   │  │ :11434  │  │ /data  │            │
│              └─────────┘  └─────────┘  └────────┘            │
│                                                              │
│  Observability                                               │
│  Prometheus :9090  →  Grafana :3000                          │
│  sqlite-web :8080     Qdrant UI :6333/dashboard              │
└──────────────────────────────────────────────────────────────┘
```

**Legacy chunking flow:** Register a PDF via the admin API → saved to the books volume → then call `POST /admin/books/{id}/reingest` → SHA-256 hashed → tracked in SQLite → PdfPig extracts pages → DndChunker splits into semantic chunks (spells, monsters, classes, etc.) → Ollama embeds each chunk → stored in Qdrant.

**LLM extraction flow (two-pass):** `POST /admin/books/{id}/extract` → PdfPig extracts pages → Ollama (`llama3.2`) classifies each page → Ollama extracts typed entities (Spell, Monster, Class, etc.) → saved as JSON files on disk → merge pass joins entities split across page boundaries → `POST /admin/books/{id}/ingest-json` → embed each entity description → stored in Qdrant with clean metadata.

**Retrieval flow:** `GET /retrieval/search?q=...` → query embedded by Ollama → Qdrant nearest-neighbour search → top-K results returned with source book, page, and category metadata.

## Prerequisites

| Tool | Version | Purpose |
| --- | --- | --- |
| [Docker](https://docs.docker.com/get-docker/) | 27+ | Container runtime |
| [docker compose](https://docs.docker.com/compose/) | v2 (CLI plugin) | Stack orchestration |
| [git-crypt](https://github.com/AGWA/git-crypt) | any | Decrypt config files |
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.0 | Local development without Docker |
| [Node.js](https://nodejs.org/) | 24+ | openspec CLI |
| [git](https://git-scm.com/) | any | Version control |

## Claude Code Setup

This project uses [Claude Code](https://claude.ai/code) with a specific set of plugins for AI-assisted development.

### 1. Install Claude Code CLI

Follow the [official installation guide](https://claude.ai/code).

### 2. Install openspec CLI

openspec manages specs and change proposals in `openspec/`.

```bash
npm install -g openspec
```

### 3. Add the dotnet-agent-skills marketplace

Add this to your `~/.claude/settings.json` under `extraKnownMarketplaces`:

```json
"extraKnownMarketplaces": {
  "dotnet-agent-skills": {
    "source": {
      "source": "github",
      "repo": "dotnet/skills"
    }
  }
}
```

### 4. Install user-scoped plugins

Run these once — they apply to all your projects:

```text
/plugin install superpowers@claude-plugins-official
/plugin install serena@claude-plugins-official
```

### 5. Install project-scoped plugins

Run these inside the repository root:

```text
/plugin install csharp-lsp@claude-plugins-official
/plugin install dotnet-ai@dotnet-agent-skills
/plugin install dotnet@dotnet-agent-skills
/plugin install dotnet-aspnet@dotnet-agent-skills
/plugin install dotnet-data@dotnet-agent-skills
/plugin install dotnet-diag@dotnet-agent-skills
/plugin install dotnet-msbuild@dotnet-agent-skills
/plugin install dotnet-nuget@dotnet-agent-skills
/plugin install dotnet-template-engine@dotnet-agent-skills
/plugin install dotnet-test@dotnet-agent-skills
/plugin install dotnet-upgrade@dotnet-agent-skills
```

### 6. Install the C# language server

The `csharp-lsp` plugin requires `csharp-ls` to be installed and on your PATH:

```bash
dotnet tool install --global csharp-ls
```

Verify it is available:

```bash
csharp-ls --version
```

Claude Code will automatically connect to it when you open `.cs` files, providing diagnostics, go-to-definition, and inline error reporting.

## Running Locally

### 1. Unlock encrypted config

Config files are encrypted with [git-crypt](https://github.com/AGWA/git-crypt). Unlock them before running the stack:

```bash
git-crypt unlock
```

This decrypts `Config/appsettings.Production.json` (which contains the admin API key and other production secrets).

### 2. Start the stack

```bash
./start.sh Development   # loads Config/appsettings.Development.json
./start.sh Production    # loads Config/appsettings.Production.json
```

Both commands run `docker compose up --build -d` (detached). The app will be available at `http://localhost:5101` once all health checks pass.

To follow logs:

```bash
docker compose logs -f app
```

To stop:

```bash
docker compose down
```

## API Reference

All endpoints are on `http://localhost:5101`.

### Health

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/health` | Liveness check |
| `GET` | `/ready` | Readiness check (Qdrant + Ollama) |

### Admin — Book Management

Requires header `X-Admin-Api-Key: <admin key>`.

| Method | Path | Description |
| --- | --- | --- |
| `POST` | `/admin/books/register` | Upload a PDF and save it as `Pending`. Does **not** start ingestion. Form fields: `file` (PDF), `sourceName`, `version` (`Edition2014` or `Edition2024`), `displayName`. Call `/reingest`, `/extract`, or `/ingest-json` to start the pipeline. |
| `POST` | `/admin/books/register-path` | Register a PDF already on the server by path. Stays `Pending` — no pipeline fires automatically. JSON body: `filePath`, `sourceName`, `version`, `displayName`. |
| `GET` | `/admin/books` | List all registered books and their ingestion status |
| `POST` | `/admin/books/{id}/reingest` | Reset a book to `Pending` and trigger re-ingestion |
| `POST` | `/admin/books/{id}/extract` | **LLM extraction Stage 1** — classify and extract entities from each page into JSON files (background, returns 202) |
| `GET` | `/admin/books/{id}/extracted` | List the JSON page files produced by Stage 1 |
| `POST` | `/admin/books/{id}/ingest-json` | **LLM extraction Stage 2** — embed extracted entities and upsert to Qdrant (background, returns 202) |
| `DELETE` | `/admin/books/{id}` | Delete a book: removes vectors from Qdrant, extracted JSON files, PDF from disk, and SQLite record |

### Retrieval

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/retrieval/search` | Semantic search. Query params: `q` (required), `version`, `category`, `sourceBook`, `entityName`, `topK` (default 5) |
| `GET` | `/admin/retrieval/search` | Same as above but returns full diagnostic payload including scores and Qdrant point IDs. Requires admin key. |

**Content categories for `category` param:** `Spell`, `Monster`, `Class`, `Background`, `Item`, `Rule`, `Treasure`, `Encounter`, `Trap`

**`version` values:** `Edition2014`, `Edition2024`

### Metrics

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/metrics` | Prometheus text format metrics. **Dev-only — unauthenticated. Disable in production via `OpenTelemetry:Enabled: false`.** |

## Observability

When the stack is running, these UIs are available:

| Service | URL | Description |
| --- | --- | --- |
| Grafana | <http://localhost:3000> | Pre-provisioned dashboards: .NET runtime, Qdrant, Ollama |
| Prometheus | <http://localhost:9090> | Raw metrics and query UI |
| sqlite-web | <http://localhost:8080> | Browse and query the `IngestionRecords` SQLite table |
| Qdrant UI | <http://localhost:6333/dashboard> | Browse vector collections and run test queries |

Grafana anonymous access is enabled in the dev stack (`GF_AUTH_ANONYMOUS_ORG_ROLE=Admin`). Remove this before any non-local deployment.
