# developer-onboarding Specification

## Purpose
TBD - created by archiving change readme-and-start-script. Update Purpose after archive.
## Requirements
### Requirement: README provides complete developer onboarding
The system SHALL provide a `README.md` at the project root that enables a new developer to understand, set up, and run the project without any additional context. The README SHALL NOT contain secrets, API keys, or credentials.

#### Scenario: New developer can understand the project purpose
- **WHEN** a developer reads the README Overview and Architecture sections
- **THEN** they understand that the project is a D&D RAG API that ingests PDF rulebooks, embeds them via Ollama into Qdrant, and exposes semantic search over HTTP

#### Scenario: Developer can identify all prerequisites with versions
- **WHEN** a developer reads the Prerequisites section
- **THEN** they see specific version requirements for Docker, docker compose v2, git-crypt, .NET 10 SDK, and Node 24+

#### Scenario: Developer can configure Claude Code from scratch
- **WHEN** a developer reads the Claude Code Setup section
- **THEN** they have step-by-step instructions to install: openspec CLI, user-scoped plugins (superpowers, serena), project-scoped plugins (csharp-lsp, dotnet-ai and the full dotnet-agent-skills suite), and how to install and expose the C# LSP server

#### Scenario: Developer can run the stack locally
- **WHEN** a developer reads the Running Locally section
- **THEN** they know to run `git-crypt unlock` first, then `./start.sh Development` or `./start.sh Production`, and understand what each environment configures differently

#### Scenario: Developer can find all API endpoints
- **WHEN** a developer reads the API Reference section
- **THEN** they see all endpoints documented: health checks (`/health`, `/ready`), admin book management (`POST /admin/books/register`, `GET /admin/books`, `POST /admin/books/{id}/reingest`), retrieval search (`GET /retrieval/search`, `GET /admin/retrieval/search`), and metrics (`GET /metrics`)

#### Scenario: Developer can find all observability UIs
- **WHEN** a developer reads the Observability section
- **THEN** they see URLs for Grafana (:3000), Prometheus (:9090), sqlite-web (:8080), and Qdrant UI (:6333/dashboard)

