## ADDED Requirements

### Requirement: No weak credentials seeded outside Development

The application SHALL NOT create any account with a fixed, publicly-known password when running
outside the Development environment. Any development seed account SHALL be gated behind
`IHostEnvironment.IsDevelopment()`. (SEC-02)

#### Scenario: Production start does not create a test account
- **WHEN** the app runs with `ASPNETCORE_ENVIRONMENT=Production` and an empty user table
- **THEN** no `test` account exists and `/login` with `test`/`test` is rejected

#### Scenario: Development still gets a convenience account
- **WHEN** the app runs in Development
- **THEN** the seeded development account is created as before

### Requirement: Character-scoped MCP tools authorize by caller identity

MCP tools that read user-owned data (hero snapshots, character sheets) SHALL resolve only data
belonging to the calling user, not an arbitrary id supplied by the caller. A request for another
user's snapshot id SHALL be denied or return no data. (SEC-08)

#### Scenario: Cross-tenant snapshot access is denied
- **WHEN** `resolve_character_feature` is invoked with a `heroSnapshotId` owned by a different user
- **THEN** it returns no character data (authorization failure), not the other user's facts

### Requirement: Public retrieval endpoints are rate-limited and bounded

The anonymous `/retrieval/*` endpoints SHALL be covered by a per-client-partitioned rate-limit
policy, and user-controlled result counts (`topK`) SHALL be clamped to a configured maximum before
driving embedding or vector search. (SEC-10)

#### Scenario: Excessive topK is clamped
- **WHEN** a caller requests `/retrieval/search` with `topK` above the configured cap
- **THEN** the effective result count is clamped to the cap

#### Scenario: Anonymous flood is throttled per client
- **WHEN** one client exceeds the retrieval rate limit
- **THEN** its further requests are rejected with 429 while other clients are unaffected

### Requirement: Rate limiting is per-client and proxy-aware

Rate-limit policies SHALL partition by client identity (authenticated user or real client IP), not a
single global bucket, and the app SHALL honour configured forwarded headers so the real client IP is
used behind the reverse proxy. Every defined rate-limit policy SHALL be applied to at least one
endpoint, and per-client limiter state SHALL be bounded (evicted) over time. (SEC-04, SEC-05,
SEC-06, SEC-07)

#### Scenario: One abuser does not lock out everyone
- **WHEN** a single client saturates its limit
- **THEN** other clients continue to be served (separate partitions)

#### Scenario: Real client IP is used behind the proxy
- **WHEN** requests arrive through the trusted reverse proxy with forwarded headers
- **THEN** limiter keys reflect the real client IP, not the proxy IP

#### Scenario: Limiter state does not grow unbounded
- **WHEN** many distinct clients make requests over time
- **THEN** stale per-client counters are evicted rather than retained indefinitely

### Requirement: Session cookie and transport hardening

The authentication cookie SHALL be issued with `SecurePolicy = Always` and an explicit `SameSite`
setting, and unauthenticated access to authorized routes SHALL redirect to the login path rather than
rendering nothing. (SEC-01, SEC-03)

#### Scenario: Cookie carries Secure and SameSite
- **WHEN** a user signs in
- **THEN** the auth cookie is emitted with the `Secure` attribute and an explicit `SameSite` value

#### Scenario: Unauthenticated user is redirected
- **WHEN** an unauthenticated user requests an authorized page
- **THEN** they are redirected to `/login` (not shown a blank `AuthorizeRouteView`)

### Requirement: No weak default secrets and no public metrics

Host-facing configuration SHALL NOT provide weak, repo-committed fallback values for admin or MCP API
keys; startup SHALL fail fast when such keys are unset. The `/metrics` endpoint SHALL NOT be reachable
by anonymous internet clients. (SEC-09, COR-24)

#### Scenario: Missing admin key fails fast
- **WHEN** the app is started without `Admin__ApiKey` set
- **THEN** startup fails rather than falling back to a committed default key

#### Scenario: Metrics are not publicly exposed
- **WHEN** metrics are enabled
- **THEN** `/metrics` is bound to a non-public interface or requires authorization

### Requirement: Auth-critical paths are tested

The suite SHALL cover the fail-closed behaviour of admin-key auth, the error paths of password
verification, and concurrent access to the chat rate limiter. (COR-02, COR-05, COR-06)

#### Scenario: Admin middleware fails closed on empty key
- **WHEN** `AdminOptions.ApiKey` is null or empty
- **THEN** requests (with or without a header) are rejected 401

#### Scenario: Password verify tolerates malformed hashes
- **WHEN** `PasswordHasher.Verify` is given an empty or malformed stored hash
- **THEN** it returns false without throwing

#### Scenario: Rate limiter is correct under concurrency
- **WHEN** N parallel acquisitions target one client with a limit below N
- **THEN** exactly the limit succeed
