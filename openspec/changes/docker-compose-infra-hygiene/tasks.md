## 1. Container image

- [x] 1.1 Add a non-root `USER` and `chown` writable paths in the runtime stage (COR-12, `Dockerfile:10`)
- [x] 1.2 Declare a `HEALTHCHECK` hitting `/ready` (COR-12, `Dockerfile`)
- [x] 1.3 Reorder to copy csproj/props → `restore` → `COPY . .` → publish `--no-restore` (COR-11, `Dockerfile:4`)

## 2. Compose

- [x] 2.1 Mount or bake the `5etools` directory in `docker-compose.prod.yml`; log a warning when absent (COR-23, `docker-compose.prod.yml:20`)
- [x] 2.2 Point the compose healthcheck at `/ready` instead of a bare TCP connect (COR-25, `docker-compose.yml:36`)

## 3. Contract parity

- [x] 3.1 Add `POST /admin/canonical/normalize` to `dnd-mcp-api.insomnia.json`; confirm `.http` parity (COR-22)

## 4. Verify + close

- [x] 4.1 Build the image, confirm non-root + healthcheck deps (COR-11/12) — real `docker build` succeeds (multi-stage restore-cache layer works; 1.08GB image); verified in-image: runs as non-root `app` (uid 1654), `curl` present for the HEALTHCHECK, `/app`/`/books`/`/data` owned by `app`. Also tightened `.dockerignore` (excluded runtime-volume dirs `books/` 732M, `5etools/` 106M, `models/` 88M, test project, tooling) — build context dropped ~1GB → 15M. Full compose stack run (with Qdrant/Ollama/Postgres + live 5etools import) still needs the real services up; the image itself is verified.
- [x] 4.2 Confirm each finding (COR-11/12/22/23/25) is addressed
