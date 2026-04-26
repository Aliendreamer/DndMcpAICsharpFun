# README and Start Script Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a comprehensive README, a `start.sh` launcher script, and clean up `docker-compose.yml` so any developer can onboard, configure Claude Code, and run the stack from a single document.

**Architecture:** Three independent file changes: `docker-compose.yml` loses two hardcoded env var lines; `start.sh` is a new 15-line bash script; `README.md` is rewritten from scratch covering all seven sections agreed in design. No code changes to the application itself.

**Tech Stack:** Bash, Markdown, Docker Compose v2

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `docker-compose.yml` | Modify (lines 8-9 of app service) | Dynamic env var, remove Admin__ApiKey |
| `start.sh` | Create | Stack launcher with env validation |
| `README.md` | Rewrite | Full developer onboarding doc |

---

### Task 1: Fix docker-compose.yml

**Files:**
- Modify: `docker-compose.yml` (app service environment block)

- [ ] **Step 1.1: Replace the hardcoded environment block**

The current `app` service environment block is:
```yaml
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Admin__ApiKey=${ADMIN_API_KEY}
```

Replace it with:
```yaml
    environment:
      - ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT}
```

- [ ] **Step 1.2: Verify config parses**

Run:
```bash
docker compose config --quiet 2>&1
```

Expected: exits 0. The only output may be a warning about `ASPNETCORE_ENVIRONMENT` not being set in the shell — that is expected and harmless (it will be set by `start.sh` at runtime).

---

### Task 2: Create start.sh

**Files:**
- Create: `start.sh`

- [ ] **Step 2.1: Create the file**

Create `/home/aliendreamer/projects/DndMcpAICsharpFun/start.sh` with this exact content:

```bash
#!/usr/bin/env bash
set -euo pipefail

ENV=${1:?"Usage: ./start.sh <Development|Production>"}

if [[ "$ENV" != "Development" && "$ENV" != "Production" ]]; then
  echo "Error: environment must be Development or Production"
  exit 1
fi

export ASPNETCORE_ENVIRONMENT="$ENV"
docker compose up --build -d
```

- [ ] **Step 2.2: Make it executable**

```bash
chmod +x start.sh
```

- [ ] **Step 2.3: Verify error cases work**

```bash
# No argument — should print usage and exit non-zero
./start.sh 2>&1 || true
```
Expected output contains: `Usage: ./start.sh <Development|Production>`

```bash
# Invalid argument — should print error and exit non-zero
./start.sh staging 2>&1 || true
```
Expected output: `Error: environment must be Development or Production`

---

### Task 3: Write README — Overview and Architecture

**Files:**
- Modify: `README.md`

- [ ] **Step 3.1: Write the full Overview and Architecture sections**

Replace the entire contents of `README.md` with the following (subsequent tasks will append):

```markdown
# DndMcpAICsharpFun

A D&D-themed ASP.NET Core Web API on .NET 10 that ingests D&D rulebook PDFs, embeds their content via [Ollama](https://ollama.ai) into a [Qdrant](https://qdrant.tech) vector store, and exposes semantic search over HTTP — enabling AI assistants to query D&D rules, spells, monsters, and more via RAG (Retrieval-Augmented Generation).

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                      Docker Compose Stack                     │
│                                                               │
│   ┌─────────────────────────────────────────────────────┐   │
│   │              ASP.NET Core app  :5101                 │   │
│   │                                                       │   │
│   │  Admin API          Ingestion Pipeline   Retrieval   │   │
│   │  /admin/books  →  Background Service  →  /retrieval  │   │
│   │  (register,        (PDF extract,        /search      │   │
│   │   list, reingest)   chunk, embed)                    │   │
│   └────────────────┬────────────┬────────────────────────┘   │
│                    │            │                             │
│              ┌─────▼──┐  ┌─────▼──┐  ┌────────┐            │
│              │ Qdrant  │  │ Ollama  │  │ SQLite │            │
│              │ :6333   │  │ :11434  │  │ /data  │            │
│              └─────────┘  └─────────┘  └────────┘            │
│                                                               │
│  Observability                                                │
│  Prometheus :9090  →  Grafana :3000                          │
│  sqlite-web :8080     Qdrant UI :6333/dashboard              │
└──────────────────────────────────────────────────────────────┘
```

**Ingestion flow:** A PDF is registered via the admin API → saved to the books volume → SHA-256 hashed → tracked in SQLite → background service picks it up → PdfPig extracts pages → DndChunker splits into semantic chunks (spells, monsters, classes, etc.) → Ollama embeds each chunk → stored in Qdrant.

**Retrieval flow:** `GET /retrieval/search?q=...` → query embedded by Ollama → Qdrant nearest-neighbour search → top-K results returned with source book, page, and category metadata.
```

- [ ] **Step 3.2: Verify the file was written**

```bash
head -5 README.md
```
Expected: `# DndMcpAICsharpFun`

---

### Task 4: Write README — Prerequisites and Claude Code Setup

**Files:**
- Modify: `README.md` (append)

- [ ] **Step 4.1: Append the Prerequisites section**

Append to `README.md`:

```markdown

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| [Docker](https://docs.docker.com/get-docker/) | 27+ | Container runtime |
| [docker compose](https://docs.docker.com/compose/) | v2 (CLI plugin) | Stack orchestration |
| [git-crypt](https://github.com/AGWA/git-crypt) | any | Decrypt config files |
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.0 | Local development without Docker |
| [Node.js](https://nodejs.org/) | 24+ | openspec CLI |
| [git](https://git-scm.com/) | any | Version control |
```

- [ ] **Step 4.2: Append the Claude Code Setup section**

Append to `README.md`:

```markdown

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

```
/plugin install superpowers@claude-plugins-official
/plugin install serena@claude-plugins-official
```

### 5. Install project-scoped plugins

Run these inside the repository root:

```
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
```

---

### Task 5: Write README — Running Locally and API Reference

**Files:**
- Modify: `README.md` (append)

- [ ] **Step 5.1: Append the Running Locally section**

Append to `README.md`:

```markdown

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
```

- [ ] **Step 5.2: Append the API Reference section**

Append to `README.md`:

```markdown

## API Reference

All endpoints are on `http://localhost:5101`.

### Health

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/health` | Liveness check |
| `GET` | `/ready` | Readiness check (Qdrant + Ollama) |

### Admin — Book Management

Requires header `X-Api-Key: <admin key>`.

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/admin/books/register` | Upload a PDF and register it for ingestion. Form fields: `file` (PDF), `sourceName`, `version` (`Edition2014` or `Edition2024`), `displayName` |
| `GET` | `/admin/books` | List all registered books and their ingestion status |
| `POST` | `/admin/books/{id}/reingest` | Reset a book to `Pending` and trigger re-ingestion |

### Retrieval

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/retrieval/search` | Semantic search. Query params: `q` (required), `version`, `category`, `sourceBook`, `entityName`, `topK` (default 5) |
| `GET` | `/admin/retrieval/search` | Same as above but returns full diagnostic payload including scores and Qdrant point IDs. Requires admin key. |

**Content categories for `category` param:** `Spell`, `Monster`, `Class`, `Background`, `Item`, `Rule`, `Treasure`, `Encounter`, `Trap`

**`version` values:** `Edition2014`, `Edition2024`

### Metrics

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/metrics` | Prometheus text format metrics. **Dev-only — unauthenticated. Disable in production via `OpenTelemetry:Enabled: false`.** |
```

---

### Task 6: Write README — Observability

**Files:**
- Modify: `README.md` (append)

- [ ] **Step 6.1: Append the Observability section**

Append to `README.md`:

```markdown

## Observability

When the stack is running, these UIs are available:

| Service | URL | Description |
|---------|-----|-------------|
| Grafana | http://localhost:3000 | Pre-provisioned dashboards: .NET runtime, Qdrant, Ollama |
| Prometheus | http://localhost:9090 | Raw metrics and query UI |
| sqlite-web | http://localhost:8080 | Browse and query the `IngestionRecords` SQLite table |
| Qdrant UI | http://localhost:6333/dashboard | Browse vector collections and run test queries |

Grafana anonymous access is enabled in the dev stack (`GF_AUTH_ANONYMOUS_ORG_ROLE=Admin`). Remove this before any non-local deployment.
```

- [ ] **Step 6.2: Verify the README looks complete**

```bash
wc -l README.md
```
Expected: 130+ lines.

```bash
grep "^## " README.md
```
Expected output:
```
## Architecture
## Prerequisites
## Claude Code Setup
## Running Locally
## API Reference
## Observability
```

---

### Task 7: Commit

**Files:** All modified/created files

- [ ] **Step 7.1: Stage and commit**

```bash
git add README.md start.sh docker-compose.yml
git commit -m "docs: add README, start.sh, and clean up docker-compose env vars"
```

Expected: commit succeeds, 3 files changed.
