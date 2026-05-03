# docker-stack (delta)

## ADDED Requirements

### Requirement: docker-compose includes a docling service
The `docker-compose.yml` and `docker-compose.prod.yml` files SHALL include a `docling` service running a tagged release of `docling-serve-cpu`, exposing the docling-serve HTTP API on the internal Docker network. The service SHALL define a healthcheck that returns success when the API responds, with a `start_period` long enough to cover model load (at least 60 seconds). The `app` service SHALL declare `docling` as a dependency with `condition: service_healthy`.

#### Scenario: Stack comes up cleanly from a fresh volume
- **WHEN** `docker compose up -d` is invoked with no pre-existing volumes
- **THEN** the `docling` container starts, completes model load, becomes healthy, and only then does `app` start

#### Scenario: docling failure prevents app startup
- **WHEN** the docling image is corrupt or the container fails to become healthy
- **THEN** `app` does not start; `docker compose ps` shows docling in unhealthy state and app in created/blocked state

### Requirement: docling-serve is CPU-only and tagged
The docling service SHALL use the CPU image variant (`docling-serve-cpu`) to avoid contending with the Ollama service for GPU resources, and SHALL pin a specific image tag (not `latest`) to prevent silent upstream upgrades from breaking the integration.

#### Scenario: GPU is reserved exclusively for Ollama
- **WHEN** `docker compose config` is rendered
- **THEN** the `docling` service has no `deploy.resources.reservations.devices` entry; only `ollama` reserves the NVIDIA device
