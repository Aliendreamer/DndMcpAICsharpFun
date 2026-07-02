## Why

The audit's infrastructure findings cluster around the container image and compose files. The runtime
container runs as **root** with no `HEALTHCHECK`; the Dockerfile copies full source before `restore`,
defeating layer caching; the production compose never mounts the `5etools` data directory, so import
and spell-backfill silently no-op and report success; the dev healthcheck only probes the TCP port
rather than the readiness endpoint; and the insomnia collection has drifted from the registered
routes. These are all deployment-hygiene fixes best shipped together.

Closes audit findings: **COR-11, COR-12, COR-22, COR-23, COR-25**.

## What Changes

- **Non-root container + healthcheck (COR-12):** add a non-privileged `USER` (e.g. `$APP_UID`),
  `chown` the writable paths, and declare a `HEALTHCHECK` hitting `/ready`.
- **Cacheable restore (COR-11):** copy only the `.csproj`/props, `dotnet restore`, then `COPY . .`
  and publish `--no-restore`.
- **Production 5etools data (COR-23):** mount or bake the `5etools` directory in
  `docker-compose.prod.yml`, and log a warning when it is missing instead of silently returning empty.
- **Readiness-based healthcheck (COR-25):** point the compose healthcheck at `/ready` (or `/health`)
  rather than a bare TCP connect.
- **Contract sync (COR-22):** add the missing `/admin/canonical/normalize` request to the insomnia
  collection (and confirm `.http` parity per the repo rule).

## Capabilities

### New Capabilities

- `deployment-infra`: the container image and compose deployment contract — process privilege,
  health/readiness probing, build-layer caching, required data mounts, and API-collection parity.

### Modified Capabilities

<!-- None; first spec to formalize the deployment-infra contract. -->

## Impact

- Modified: `Dockerfile` (USER, HEALTHCHECK, restore ordering), `docker-compose.yml` (healthcheck
  target), `docker-compose.prod.yml` (5etools mount), `dnd-mcp-api.insomnia.json` (+ `.http` if
  needed), and a startup warning when the 5etools directory is absent.
- No code-behaviour or data-model changes beyond the missing-directory warning log.

## Non-goals

- Introducing a separate hardened production compose beyond the mount/hygiene fixes (that is tracked
  elsewhere in the roadmap).
- Kubernetes/orchestration manifests.
