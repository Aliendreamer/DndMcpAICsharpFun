# http-contracts Specification

## Purpose

Defines the contract for the `DndMcpAICsharpFun.http` file — the canonical HTTP request collection that covers every registered route in the project. This spec ensures the file remains complete and stays in sync with route registrations as the API evolves.

## Requirements

### Requirement: DndMcpAICsharpFun.http covers all registered endpoints
The system SHALL provide a `DndMcpAICsharpFun.http` file at the project root containing at least one example HTTP request for every registered route. The file SHALL define `@baseUrl` and `@adminKey` variables at the top and use them in all requests.

#### Scenario: Health endpoints are present
- **WHEN** a developer opens `DndMcpAICsharpFun.http`
- **THEN** it contains example requests for `GET /health`, `GET /ready`, and `GET /health/ready`

#### Scenario: Admin book lifecycle endpoints are present with auth header
- **WHEN** a developer opens `DndMcpAICsharpFun.http`
- **THEN** it contains example requests for `POST /admin/books/register` (multipart upload), `GET /admin/books`, `POST /admin/books/{id}/ingest-blocks`, and `DELETE /admin/books/{id}`, each including an `X-Admin-Api-Key: {{adminKey}}` header

#### Scenario: Retrieval endpoints are present
- **WHEN** a developer opens `DndMcpAICsharpFun.http`
- **THEN** it contains example requests for `GET /retrieval/search` and `GET /admin/retrieval/search` with representative query parameters (`q`, `version`, `category`, `topK`)

#### Scenario: Metrics endpoint is present
- **WHEN** a developer opens `DndMcpAICsharpFun.http`
- **THEN** it contains an example request for `GET /metrics`

### Requirement: DndMcpAICsharpFun.http is kept in sync with route registrations
The system SHALL maintain `DndMcpAICsharpFun.http` as an accurate reflection of all registered routes. Any commit that adds, modifies, or removes a route (`MapGet`, `MapPost`, `MapPut`, `MapDelete`) SHALL also update `DndMcpAICsharpFun.http` accordingly. This requirement is enforced via the CLAUDE.md API Contracts rule.

#### Scenario: New endpoint is added
- **WHEN** a new route is registered in any endpoint file
- **THEN** a corresponding example request is added to `DndMcpAICsharpFun.http` in the same commit

#### Scenario: Endpoint path is changed
- **WHEN** an existing route path is modified
- **THEN** the corresponding entry in `DndMcpAICsharpFun.http` is updated to reflect the new path in the same commit

#### Scenario: Endpoint is removed
- **WHEN** a route registration is deleted
- **THEN** the corresponding entry is removed from `DndMcpAICsharpFun.http` in the same commit

### Requirement: Removed admin endpoints are absent from .http and from routing
The `DndMcpAICsharpFun.http` file SHALL NOT contain example blocks for `POST /admin/books/{id}/extract`, `POST /admin/books/{id}/ingest-json`, `POST /admin/books/{id}/extract-page/{pageNumber}`, or `POST /admin/books/{id}/cancel-extract`. The application SHALL respond with HTTP 404 to any request against those routes.

#### Scenario: Calls to removed routes return 404
- **WHEN** any HTTP method is issued against `/admin/books/{id}/extract`, `/admin/books/{id}/ingest-json`, `/admin/books/{id}/extract-page/{n}`, or `/admin/books/{id}/cancel-extract`
- **THEN** the application returns HTTP 404 because the route is no longer registered

#### Scenario: .http file does not document removed endpoints
- **WHEN** the `.http` file is searched for `extract`, `ingest-json`, `extract-page`, or `cancel-extract`
- **THEN** the only matches are the surviving routes and no example block is dedicated to a removed route
