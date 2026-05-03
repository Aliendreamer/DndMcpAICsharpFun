# http-contracts (delta)

## ADDED Requirements

### Requirement: Removed admin endpoints are absent from .http and from routing
The `DndMcpAICsharpFun.http` file SHALL NOT contain example blocks for `POST /admin/books/{id}/extract`, `POST /admin/books/{id}/ingest-json`, `POST /admin/books/{id}/extract-page/{pageNumber}`, or `POST /admin/books/{id}/cancel-extract`. The application SHALL respond with HTTP 404 to any request against those routes.

#### Scenario: .http file does not document removed endpoints
- **WHEN** the `.http` file is searched for `extract`, `ingest-json`, `extract-page`, or `cancel-extract`
- **THEN** the only matches are the surviving routes (`/ingest-blocks` is acceptable; `extract` as a substring of other words is not relevant) and no example block is dedicated to a removed route

#### Scenario: Calls to removed routes return 404
- **WHEN** any HTTP method is issued against `/admin/books/{id}/extract`, `/admin/books/{id}/ingest-json`, `/admin/books/{id}/extract-page/{n}`, or `/admin/books/{id}/cancel-extract`
- **THEN** the application returns HTTP 404 Not Found because the route is no longer registered

### Requirement: .http file documents only the surviving admin lifecycle
The `.http` file SHALL contain example blocks for `POST /admin/books/register`, `GET /admin/books`, `POST /admin/books/{id}/ingest-blocks`, and `DELETE /admin/books/{id}` (plus any retrieval / observability blocks unrelated to ingestion). The collection-selection comment near retrieval examples is removed.

#### Scenario: Lifecycle is register → ingest-blocks → delete
- **WHEN** a reader follows the `.http` file in order
- **THEN** the documented admin lifecycle is `register` (multipart upload) → `ingest-blocks` (no-LLM end-to-end) → optional `delete`, with no intermediate stages
