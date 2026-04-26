## 1. docker-compose.yml Cleanup

- [ ] 1.1 Replace `ASPNETCORE_ENVIRONMENT=Production` with `ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT}` in the `app` service environment block
- [ ] 1.2 Remove `Admin__ApiKey=${ADMIN_API_KEY}` from the `app` service environment block
- [ ] 1.3 Run `docker compose config --quiet` — verify it parses without errors

## 2. start.sh

- [ ] 2.1 Create `start.sh` at the project root with `#!/usr/bin/env bash` and `set -euo pipefail`
- [ ] 2.2 Add argument validation: fail with usage message if no argument given; fail with error if value is not `Development` or `Production`
- [ ] 2.3 Export `ASPNETCORE_ENVIRONMENT` and run `docker compose up --build -d`
- [ ] 2.4 `chmod +x start.sh`

## 3. README.md

- [ ] 3.1 Write Overview section: what the project is and what it does (D&D RAG API, ingestion, embedding, retrieval)
- [ ] 3.2 Write Architecture section: ASCII/text diagram covering ingestion pipeline → Qdrant → retrieval, admin endpoints, background service, observability stack
- [ ] 3.3 Write Prerequisites section: Docker 27+, docker compose v2, git-crypt, .NET 10 SDK, Node 24+, git — with versions
- [ ] 3.4 Write Claude Code Setup section: install Claude Code CLI, openspec CLI (`npm install -g openspec`), user-scoped plugins (superpowers, serena), project-scoped plugins (csharp-lsp, dotnet-ai, full dotnet-agent-skills suite), the dotnet-agent-skills marketplace config, csharp-lsp server installation and exposure
- [ ] 3.5 Write Running Locally section: `git-crypt unlock` first, then `./start.sh Development` or `./start.sh Production`, what each environment loads
- [ ] 3.6 Write API Reference section: health checks (`GET /health`, `GET /ready`), admin endpoints (`POST /admin/books/register`, `GET /admin/books`, `POST /admin/books/{id}/reingest`), retrieval (`GET /retrieval/search`, `GET /admin/retrieval/search`), metrics (`GET /metrics`, with the dev-only caveat)
- [ ] 3.7 Write Observability section: table of Grafana (:3000), Prometheus (:9090), sqlite-web (:8080), Qdrant UI (:6333/dashboard)

## 4. Commit

- [ ] 4.1 Commit all changes: `docs: add README, start.sh, and clean up docker-compose env vars`
