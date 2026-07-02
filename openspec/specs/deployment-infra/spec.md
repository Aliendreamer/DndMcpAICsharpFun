# deployment-infra Specification

## Purpose
TBD - created by archiving change docker-compose-infra-hygiene. Update Purpose after archive.
## Requirements
### Requirement: Container runs unprivileged with a health probe

The runtime image SHALL run the application as a non-root user and SHALL declare a `HEALTHCHECK` that
exercises the app's readiness endpoint. Writable paths SHALL be owned by the runtime user. (COR-12)

#### Scenario: Process is not root
- **WHEN** the container is running
- **THEN** the app process runs as a non-root user

#### Scenario: Health is probed via the app
- **WHEN** the orchestrator checks container health
- **THEN** the `HEALTHCHECK` calls `/ready` (or `/health`) and reflects app readiness

### Requirement: Build uses cached restore

The Dockerfile SHALL restore dependencies in a layer that depends only on the project/props files, so
that source-only changes do not invalidate the restore cache. (COR-11)

#### Scenario: Source change reuses restore
- **WHEN** only source files change between builds
- **THEN** the `dotnet restore` layer is served from cache

### Requirement: Production provides the 5etools data directory

The production deployment SHALL make the `5etools` data directory available to the app (mounted or
baked), and the app SHALL log a warning when the directory is absent rather than silently returning
empty results from import/backfill paths. (COR-23)

#### Scenario: Production import has its data
- **WHEN** the production stack runs and a 5etools import is requested
- **THEN** the data directory is present and the import operates on real data

#### Scenario: Missing directory is surfaced
- **WHEN** the 5etools directory is absent at startup
- **THEN** a warning is logged (not a silent success with empty results)

### Requirement: Compose healthcheck reflects readiness

The compose healthcheck SHALL probe the application readiness/health endpoint rather than a bare TCP
port connection. (COR-25)

#### Scenario: Healthcheck fails when app is not ready
- **WHEN** the port is open but the app is not ready
- **THEN** the healthcheck reports unhealthy (because it probes `/ready`, not the socket)

### Requirement: API collection matches registered routes

The importable API collection SHALL contain a request for every registered route, including
`POST /admin/canonical/normalize`, keeping `dnd-mcp-api.insomnia.json` in sync with the `.http` file
and the registered endpoints. (COR-22)

#### Scenario: Every route has a collection entry
- **WHEN** the registered routes are compared to the collection
- **THEN** `/admin/canonical/normalize` (and all others) have a corresponding request

