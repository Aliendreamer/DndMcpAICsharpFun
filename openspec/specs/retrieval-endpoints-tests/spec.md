# retrieval-endpoints-tests Specification

## Purpose
TBD - created by archiving change test-coverage-wave-2. Update Purpose after archive.
## Requirements
### Requirement: Public search endpoint validates required query parameter
`GET /retrieval/search` SHALL return 400 Bad Request when the `q` parameter is missing or whitespace-only.

#### Scenario: Missing q returns 400
- **WHEN** `GET /retrieval/search` is called without the `q` query parameter
- **THEN** the response status is 400

#### Scenario: Whitespace-only q returns 400
- **WHEN** `GET /retrieval/search` is called with `q=   `
- **THEN** the response status is 400

### Requirement: Public search endpoint returns results
`GET /retrieval/search` SHALL call `IRagRetrievalService.SearchAsync` and return 200 OK with the results.

#### Scenario: Valid query returns 200 with results
- **WHEN** `GET /retrieval/search?q=fireball` is called
- **THEN** `SearchAsync` is called once
- **AND** the response status is 200
- **AND** the response body contains the mocked results

### Requirement: Public search endpoint parses optional filters
`GET /retrieval/search` SHALL parse `version` and `category` query params as enum values, passing `null` for unrecognised values.

#### Scenario: Valid version and category are parsed
- **WHEN** `GET /retrieval/search?q=fireball&version=Edition2024&category=Spell` is called
- **THEN** `SearchAsync` receives a query with `Version == DndVersion.Edition2024` and `Category == ContentCategory.Spell`

#### Scenario: Invalid version and category fall back to null
- **WHEN** `GET /retrieval/search?q=fireball&version=invalid&category=invalid` is called
- **THEN** `SearchAsync` receives a query with `Version == null` and `Category == null`

### Requirement: Admin diagnostic search endpoint requires auth and returns diagnostic results
`GET /admin/retrieval/search` SHALL return 401 without the admin key, and 200 with diagnostic results when authenticated.

#### Scenario: Missing admin key returns 401
- **WHEN** `GET /admin/retrieval/search?q=fireball` is called without `X-Admin-Api-Key`
- **THEN** the response status is 401

#### Scenario: Valid admin key returns diagnostic results
- **WHEN** `GET /admin/retrieval/search?q=fireball` is called with valid `X-Admin-Api-Key`
- **THEN** `SearchDiagnosticAsync` is called once
- **AND** the response status is 200

