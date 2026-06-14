## ADDED Requirements

### Requirement: Canonical JSON is written through a single serializer

All code that writes canonical JSON files SHALL use one shared serializer configuration (indented, with the entity-type enum serialized as a string via `JsonStringEnumConverter`, trailing newline). No canonical writer SHALL define its own divergent settings, and none SHALL omit the enum converter.

#### Scenario: Entity-type enums serialize as strings everywhere

- **WHEN** any canonical-writing service (including the type fixer) writes an entity whose `type` is an `EntityType` enum
- **THEN** the written JSON SHALL encode `type` as its string name, not an integer

#### Scenario: Canonical writers share one configuration

- **WHEN** the canonical writers are inspected
- **THEN** they SHALL all use the shared canonical serializer rather than locally-defined `JsonSerializerOptions`

### Requirement: The composed DI container passes scope validation

The application's composed service registrations SHALL pass dependency-injection scope and build validation: no singleton may capture a scoped service.

#### Scenario: Container builds with scope validation enabled

- **WHEN** the application's service registrations are built with `ValidateScopes = true` and `ValidateOnBuild = true` (external clients stubbed)
- **THEN** building the provider SHALL NOT throw

### Requirement: Admin endpoints are integration-covered for binding and auth

The admin HTTP endpoints SHALL have integration tests covering query-parameter binding (including optional parameters using their defaults) and admin-key authentication, independent of the underlying service unit tests.

#### Scenario: Optional paging defaults are honored over HTTP

- **WHEN** `GET /admin/entities/needs-review` is called with no `offset`/`limit`
- **THEN** the request SHALL succeed using default paging (not fail with a missing-parameter error)

#### Scenario: Admin key is enforced

- **WHEN** an admin endpoint is called without a valid admin key
- **THEN** the response SHALL be unauthorized
