# docker-stack

## Purpose

Defines the containerisation and Docker Compose requirements for running the full application stack locally and in production.

## Requirements

### Requirement: Docker Compose defines app, Qdrant, and Ollama services
The system SHALL provide a `docker-compose.yml` that defines three services: `app` (ASP.NET Core), `qdrant` (vector store), and `ollama` (embedding model host), all on a shared internal network.

#### Scenario: Stack starts cleanly
- **WHEN** `docker compose up` is run from the project root
- **THEN** all three services start and reach a healthy state

#### Scenario: App waits for dependencies
- **WHEN** Qdrant or Ollama has not yet passed its health check
- **THEN** the `app` service does not report healthy until both dependencies are ready

### Requirement: Persistent volumes for books and Qdrant data
The system SHALL define named Docker volumes: one for the PDF books directory (mounted into `app`) and one for Qdrant storage data.

#### Scenario: Books volume is mounted in app container
- **WHEN** the `app` container starts
- **THEN** the books volume is accessible at the configured `Ingestion:BooksPath`

#### Scenario: Qdrant data survives container restart
- **WHEN** the `qdrant` container is restarted
- **THEN** previously stored collections and vectors are still present

### Requirement: Dockerfile uses multi-stage build
The system SHALL provide a `Dockerfile` with a build stage (`sdk:10.0`) and a runtime stage (`aspnet:10.0`), producing a minimal final image.

#### Scenario: Image builds successfully
- **WHEN** `docker build` is run from the project root
- **THEN** the image is produced without error and contains no SDK tooling

### Requirement: Admin API key is injected via environment variable
The system SHALL read the admin API key from the `Admin__ApiKey` environment variable in the Docker Compose service definition, not hardcoded in any image layer.

#### Scenario: Key is configurable without rebuilding
- **WHEN** the `Admin__ApiKey` environment variable is changed in Compose
- **THEN** the app uses the new key without requiring an image rebuild
