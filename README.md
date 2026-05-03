# DndMcpAICsharpFun

A D&D-themed ASP.NET Core Web API on .NET 10 that ingests D&D rulebook PDFs, embeds their content via [Ollama](https://ollama.ai) into a [Qdrant](https://qdrant.tech) vector store, and exposes semantic search over HTTP — enabling AI assistants to query D&D rules, spells, monsters, and more via RAG (Retrieval-Augmented Generation).

The ingestion pipeline uses [Docling](https://github.com/docling-project/docling) (run as a sidecar service) for layout-aware PDF extraction, so multi-column rulebooks like the PHB come out with correct reading order instead of column-scrambled text. There is no LLM call during ingestion — only an embedding call per chunk.

## Architecture

```text
┌──────────────────────────────────────────────────────────────────┐
│                        Docker Compose Stack                      │
│                                                                  │
│   ┌──────────────────────────────────────────────────────────┐   │
│   │                ASP.NET Core app  :5101                   │   │
│   │                                                          │   │
│   │   Admin API          Ingestion Pipeline    Retrieval     │   │
│   │   /admin/books   →   IngestionQueueWorker → /retrieval   │   │
│   │   (register,         (block extract,         /search     │   │
│   │    list, delete,      embed, upsert)                     │   │
│   │    ingest-blocks)                                        │   │
│   └──────┬───────────────┬───────────────┬───────────────────┘   │
│          │               │               │                       │
│   ┌──────▼─────┐  ┌──────▼─────┐  ┌──────▼─────┐  ┌─────────┐    │
│   │  docling   │  │   Ollama   │  │   Qdrant   │  │ SQLite  │    │
│   │  :5001     │  │   :11434   │  │   :6333    │  │  /data  │    │
│   │ (layout)   │  │ (embed)    │  │ (vectors)  │  │ (state) │    │
│   └────────────┘  └────────────┘  └────────────┘  └─────────┘    │
│                                                                  │
│   Observability                                                  │
│   Prometheus :9090   →   Grafana :3000                           │
│   sqlite-web :8080       Qdrant UI :6333/dashboard               │
└──────────────────────────────────────────────────────────────────┘
```

**Ingestion flow** (single-stage, no LLM):

1. `POST /admin/books/register` — multipart upload, streams the PDF straight to disk under a server-generated GUID name, persists an `IngestionRecord` (status `Pending`).
2. `POST /admin/books/{id}/ingest-blocks` — enqueues background work that:
   - Reads the PDF's bookmark tree (via PdfPig) for section/category metadata.
   - Posts the PDF to docling-serve (async + polling), gets back layout-aware blocks with correct multi-column reading order.
   - Filters out fragments (<40 chars) and pure-numeric tables.
   - Embeds each block's text via Ollama (default model `mxbai-embed-large`, inputs truncated to 1500 chars).
   - Upserts into the Qdrant `dnd_blocks` collection with full metadata payload.
   - Marks the record `JsonIngested` with the final block count.

**Retrieval flow:**

`GET /retrieval/search?q=...` → query embedded by Ollama → Qdrant nearest-neighbour search against `dnd_blocks` → top-K results returned with text, score, and metadata (source book, page, section, category, book type, edition).

**Why bookmarks for sections?** Modern D&D PDFs have an embedded outline tree that maps `(section title, start page)` for every chapter and subsection. We walk it recursively (with parent-context propagation, so MM monster names like `"Aboleth"` inherit `Monster` category from their parent `"Monsters (A-Z)"`), giving us reliable section boundaries without any LLM cost.

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

First boot is slow because:
- `ollama-pull` downloads `mxbai-embed-large` (~1 GB).
- `docling` pulls a ~3 GB image and loads its layout model on first health check (~60-90 s).

The `app` service waits for both to be healthy before starting.

To follow logs:

```bash
docker compose logs -f app
docker compose logs -f docling   # watch layout-analysis progress during ingestion
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
| `GET` | `/ready` | Readiness check (Qdrant + Ollama + docling) |

### Admin — Book Management

Requires header `X-Admin-Api-Key: <admin key>`.

| Method | Path | Description |
| --- | --- | --- |
| `POST` | `/admin/books/register` | Upload a PDF and save it as `Pending`. Streams multipart body straight to disk; returns 202. Form fields: `file` (PDF), `version` (`Edition2014` or `Edition2024`), `displayName`, optional `bookType` (`Core` / `Supplement` / `Adventure` / `Setting` / `Unknown`, default `Unknown`). Does **not** start ingestion — call `/ingest-blocks` next. |
| `GET` | `/admin/books` | List all registered books with status, displayName, version, bookType, and chunk count. |
| `POST` | `/admin/books/{id}/ingest-blocks` | Enqueue Docling layout extraction + embedding + Qdrant upsert. Returns 202; work runs in the background queue. The same call re-runs the pipeline cleanly (deletes prior points by hash). |
| `DELETE` | `/admin/books/{id}` | Remove the book: deletes Qdrant points by hash, deletes the PDF from disk, deletes the SQLite record. Returns 409 if the book is currently `Processing`. |

### Retrieval

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/retrieval/search` | Semantic search over `dnd_blocks`. Query params: `q` (required), `version`, `category`, `sourceBook`, `entityName`, `bookType`, `topK` (default 5, capped at `Retrieval:MaxTopK`). |
| `GET` | `/admin/retrieval/search` | Same as above but returns `RetrievalDiagnosticResult` including the Qdrant point ID and exact score. Requires admin key. |

**`version` values:** `Edition2014`, `Edition2024`.

**`category` values:** `Spell`, `Monster`, `Class`, `Race`, `Background`, `Item`, `Rule`, `Combat`, `Adventuring`, `Condition`, `God`, `Plane`, `Treasure`, `Encounter`, `Trap`, `Trait`, `Lore`, `Unknown`.

**`bookType` values:** `Core` (PHB/MM/DMG of any edition), `Supplement` (Volo's, Tasha's, Xanathar's, etc.), `Adventure` (Curse of Strahd, Tomb of Annihilation, etc.), `Setting` (Eberron, Wildemount, SCAG, etc.), `Unknown` (default).

Filters compose. For example, `?q=fireball&category=Spell&bookType=Core&version=Edition2014` returns only 5e Core-rulebook spell blocks containing fireball-related content.

### Metrics

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/metrics` | Prometheus text format metrics. **Dev-only — unauthenticated. Disable in production via `OpenTelemetry:Enabled: false`.** |

## How Books Are Tagged

When you register a book, set `bookType` and `version` to make filtering useful later. Suggested mapping:

| Book | `version` | `bookType` |
| --- | --- | --- |
| Player's Handbook 2014 / 2024 | `Edition2014` / `Edition2024` | `Core` |
| Monster Manual 2014 / 2024 | `Edition2014` / `Edition2024` | `Core` |
| Dungeon Master's Guide 2014 / 2024 | `Edition2014` / `Edition2024` | `Core` |
| Volo's Guide to Monsters | `Edition2014` | `Supplement` |
| Xanathar's Guide to Everything | `Edition2014` | `Supplement` |
| Tasha's Cauldron of Everything | `Edition2014` | `Supplement` |
| Mordenkainen Presents: Monsters of the Multiverse | `Edition2014` | `Supplement` |
| Curse of Strahd, Tomb of Annihilation, etc. | `Edition2014` | `Adventure` |
| Eberron: Rising from the Last War, Wildemount, SCAG | `Edition2014` | `Setting` |

Mental shortcut: `version` describes which rules system the book was published for. `bookType` describes how it relates to the rules — Core = the canonical three, Supplement = adds rules content, Adventure = a story module, Setting = world/lore.

## Ingestion Notes

- **Docling on CPU** — `docling-serve-cpu` runs without GPU contention with Ollama. A PHB-class 300-page book takes ~25-30 minutes for layout analysis on a modern multi-core CPU. Subsequent books reuse the loaded model and run at the same per-book rate.
- **Re-ingestion is idempotent** — calling `/ingest-blocks` again deletes the prior points (by file hash + global index) before upserting new ones. Safe to re-run if Docling output improves or you want to re-tag with a different `bookType`.
- **Bookmark requirement** — books without an embedded bookmark/outline tree fail with a clear error. Almost every modern WotC PDF has bookmarks; pirated/scanned versions sometimes don't. If a book lacks bookmarks, switch source.
- **Embedding truncation** — block text is pre-truncated to 1500 chars before embedding to fit `mxbai-embed-large`'s 512-token context. The full untruncated text is still stored in the Qdrant payload, so retrieval results show the complete block — only the embedding signal for very long blocks is approximate.

## Observability

When the stack is running, these UIs are available:

| Service | URL | Description |
| --- | --- | --- |
| Grafana | <http://localhost:3000> | Pre-provisioned dashboards: .NET runtime, Qdrant, Ollama |
| Prometheus | <http://localhost:9090> | Raw metrics and query UI |
| sqlite-web | <http://localhost:8080> | Browse and query the `IngestionRecords` SQLite table |
| Qdrant UI | <http://localhost:6333/dashboard> | Browse vector collections and run test queries |
| docling-serve | <http://localhost:5001/docs> | Swagger UI for the docling layout service (mostly for debugging) |

Grafana anonymous access is enabled in the dev stack (`GF_AUTH_ANONYMOUS_ORG_ROLE=Admin`). Remove this before any non-local deployment.
