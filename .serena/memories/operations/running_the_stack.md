# Running the stack (live) and verifying ingestion/retrieval

`docker-compose.yml` builds and runs **only the app** (single host: API + MCP server + Blazor UI).
All infrastructure (Postgres, Qdrant, Ollama, MinerU, SearXNG) is the EXTERNAL shared
**PersonalCommandCenter** stack on network `personalcommandcenter_default`. Bring that up first
(usually already running).

## Infra prerequisites

| Service | Container | Notes |
| --- | --- | --- |
| Postgres | `personalcommandcenter-postgres-1` | superuser `pcc`; app role **`dnd`** + database **`dnd`** |
| Qdrant | `personalcommandcenter-qdrant-1` | **not published to host** (internal network only) |
| Ollama | `personalcommandcenter-ollama-1` | needs `mxbai-embed-large` + `qwen3:8b` |
| MinerU | `personalcommandcenter-mineru-1` | PDF→structure, reached as `mineru:8000` |
| SearXNG | `personalcommandcenter-searxng-1` | web search |

Checks:

```bash
docker network ls | grep personalcommandcenter_default
docker exec personalcommandcenter-postgres-1 psql -U pcc -tAc \
  "SELECT rolname FROM pg_roles WHERE rolname='dnd'; SELECT datname FROM pg_database WHERE datname='dnd';"
docker exec personalcommandcenter-ollama-1 ollama list | grep -E "mxbai|qwen3"
```

## Run the app

`Admin__ApiKey` and `Mcp__ApiKey` are MANDATORY (hardening removed the dev fallbacks). `.env` is
git-ignored and masked in the agent sandbox, so pass keys INLINE — Compose substitutes them from the
shell env:

```bash
ADMIN_API_KEY=$(openssl rand -hex 20) \
MCP_API_KEY=$(openssl rand -hex 20) \
docker compose up -d --build app
```

(Or copy `.env.example` -> `.env` and set the two keys.) `--build` recreates the container so code
changes take effect (image otherwise lags `main`). Startup applies EF migrations to `dnd` + seeds a
`test` user. App = `http://localhost:5101`; `/health` -> `Healthy`. Save the ADMIN key — admin routes
need the `X-Admin-Api-Key` header. All `docker`/`curl localhost:5101` commands need the agent sandbox
DISABLED (docker socket + network + masked `.env`).

## Verify ingestion + retrieval

`ingest-blocks` is FAST (cached MinerU conversion + GPU embed, ~minutes); `extract-entities` is the
slow (~hours) LLM path. For BM25/retrieval checks, use blocks.

```bash
AK=<generated ADMIN_API_KEY>
curl -s -H "X-Admin-Api-Key: $AK" http://localhost:5101/admin/books      # status 4=JsonIngested, 1=Processing
curl -s -X POST -H "X-Admin-Api-Key: $AK" http://localhost:5101/admin/books/2/ingest-blocks  # 202; also updates Bm25*Stats
docker exec personalcommandcenter-postgres-1 psql -U pcc -d dnd -tAc \
  'SELECT count(*) FROM "Bm25TermStats"; SELECT "DocumentCount" FROM "Bm25CorpusStats" WHERE "Id"=1;'  # PHB ~10965 terms / 5243 docs
curl -s 'http://localhost:5101/retrieval/search?q=fireball&topK=3'        # varied scores = hybrid working
```

Notes: the book `error` field can be STALE from a prior run until the current run finishes — trust
`status` + `docker logs dndmcpaicsharpfun-app-1 --since 3m`. Qdrant is not host-published; inspect via
the app's `/retrieval/*` endpoints, or publish `127.0.0.1:6333:6333` on the qdrant service.

## BM25 corpus consistency

Sparse vectors are baked into Qdrant at ingest time using the corpus-global stats AS THEY ARE THEN. The
stats grow as books are added, so for full consistency re-ingest ALL books, then re-ingest earlier ones
once more after the last lands. `POST /admin/retrieval/bm25/rebuild-stats` recomputes the global
aggregates from the per-book contribution rows (recovery net; does NOT re-vectorize Qdrant). As of
2026-07-02 only PHB (id=2) is ingested; MM + DMG pending.

Related: `mem:project_companion_roadmap` (audit remediation Item 7), the dev-flow skill (the
EnableRetryOnFailure transaction gotcha caught during the live run).
