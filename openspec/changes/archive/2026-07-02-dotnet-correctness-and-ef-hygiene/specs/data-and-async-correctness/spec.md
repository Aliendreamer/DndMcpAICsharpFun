## ADDED Requirements

### Requirement: Multi-step writes are atomic

A multi-step write SHALL execute atomically. Any operation performing more than one dependent write
(e.g. deleting a campaign's or hero's related rows) SHALL run within a transaction or use a DB-level
cascade, so that a partial failure cannot leave orphaned rows. (NET-01, NET-03)

#### Scenario: Failed hero delete leaves no partial state
- **WHEN** the second step of a hero deletion fails
- **THEN** the first step is rolled back (no orphaned snapshots or hero row)

### Requirement: Reads avoid N+1 queries

Loading a collection with a per-item derived value (e.g. each hero's latest snapshot) SHALL use a
single set-based query, not one query per item. (NET-02)

#### Scenario: Listing heroes issues one snapshot query
- **WHEN** N heroes are listed with their latest snapshot
- **THEN** the latest snapshots are fetched in a single query, not N

### Requirement: Async I/O propagates cancellation and does not block

I/O paths SHALL propagate the caller's `CancellationToken` and SHALL NOT block on async calls
(no sync-over-async on long-running HTTP). The PDF block extractor SHALL expose an async signature.
Bulk projection SHALL batch its saves rather than saving per table. (NET-04, NET-06, NET-07)

#### Scenario: Block deletion honours cancellation
- **WHEN** the caller cancels during block deletion
- **THEN** the operation observes the token (not `CancellationToken.None`)

#### Scenario: PDF extraction does not block a thread
- **WHEN** the PDF structure conversion runs
- **THEN** it is awaited (no `GetAwaiter().GetResult()` on the long HTTP call)

### Requirement: Resources are disposed and failures surfaced

Per-key synchronization primitives SHALL be released/evicted rather than accumulated, and
client failures (transport/deserialization) SHALL be logged with the exception and distinguished from
legitimate empty results. JSON columns SHALL use a deliberately chosen mapping (`jsonb` with GIN when
server-side querying is needed, else `text`). (NET-05, NET-11, NET-12)

#### Scenario: Web-search transport failure is not hidden
- **WHEN** the web-search client hits a transport or deserialization error
- **THEN** it logs the exception and signals failure (not an empty-results success)

### Requirement: Data-path logic is correct

Canonical file deletion SHALL NOT remove another book's file when slugs collide; re-projection SHALL
remove tables/choice-sets that no longer exist in the source; ability-score lines SHALL render only
present values; malformed converter responses SHALL raise descriptive errors; and computed values
SHALL carry their own (computed) provenance, not an unrelated cell's. (COR-13, COR-18, COR-19,
COR-20, COR-21)

#### Scenario: Slug collision does not delete the wrong file
- **WHEN** two books resolve to the same canonical slug and one is deleted
- **THEN** the other book's canonical file is retained

#### Scenario: Re-projection drops removed rows
- **WHEN** a canonical file is re-projected after a table/choice-set was removed
- **THEN** the corresponding rows are deleted, not left orphaned

#### Scenario: Malformed converter response is descriptive
- **WHEN** the MinerU response is empty or shaped unexpectedly
- **THEN** a descriptive error naming the file/field is thrown (not an opaque `First()` exception)

### Requirement: Reliability-critical paths are tested cleanly

Tests for these paths SHALL avoid dead helpers, fragile relative paths, shared-temp wildcard
deletes, fixed-delay synchronization, and weak substring assertions; temp resources SHALL be isolated
and cleaned up. (COR-01, COR-03, COR-04, COR-07, COR-08, COR-09)

#### Scenario: Async dispatch test is deterministic
- **WHEN** the queue-worker dispatch test runs
- **THEN** it awaits a completion signal (not a fixed `Task.Delay`)

#### Scenario: Assertions are discriminating
- **WHEN** the resolve-feature test asserts a value
- **THEN** it asserts a specific token (e.g. `DC 15`), not a substring that could match unrelated content
