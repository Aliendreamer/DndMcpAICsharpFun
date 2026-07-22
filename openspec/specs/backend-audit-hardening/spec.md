# backend-audit-hardening Specification

## Purpose
TBD - created by archiving change audit-fixes. Update Purpose after archive.
## Requirements
### Requirement: Decline-recovery SHALL be resume-complete

The decline-recovery phase SHALL process every candidate recorded as declined in the run's audits (deterministic `declined` entries AND `extraction_declined` error entries), including candidates declined BEFORE a crash on a resumed run — never only the freshly-looped subset.

#### Scenario: A pre-crash LLM-declined candidate is recovered after resume

- **WHEN** a run resumes from a checkpoint whose errors contain an `extraction_declined` entry for a candidate
- **THEN** that candidate is included in the recovery phase (and recovered/reconciled identically to a fresh run)

### Requirement: Entity ids SHALL derive from the entity's actual type

A `ForceType`-resolved candidate SHALL be recorded with an id whose type segment matches the FORCED type (canonical name when 5etools-matched, else the display name) — an `Object`-forced entity is `<book>.object.<slug>`, never `<book>.monster.<slug>`.

#### Scenario: Object-forced candidate gets an object id

- **WHEN** a stat-block candidate is force-resolved to `Object` without a 5etools canonical name
- **THEN** its recorded id is `<book>.object.<name-slug>` and the stored `Type` matches

### Requirement: Ambiguous table resolution SHALL fail safe

When a resolver suffix lookup (`.table.<slug>` / `.table.<slug>-spells`) matches MORE than one `StructuredTables` row, the resolver SHALL return `needsReview` for that component instead of picking an arbitrary match.

#### Scenario: Cross-book same-slug tables

- **WHEN** two books both contain a table whose id ends with the resolver's suffix
- **THEN** the component resolves `needsReview` ("ambiguous"), never a silently-wrong book's data with `ok` confidence

### Requirement: Book-catalog drift SHALL fail loud

`BookCatalog` SHALL include every ingested official book, and the startup scope-health guard SHALL warn when `dnd_blocks` contains blocks whose `source_key` matches no catalog key.

#### Scenario: An ingested-but-unregistered book is detected

- **WHEN** blocks exist with a `source_key` absent from the catalog
- **THEN** startup logs a warning naming the count, instead of the book being silently invisible

### Requirement: Duplicate extraction enqueues SHALL be rejected

The ingestion queue SHALL reject enqueueing a book that is already queued or in flight; the endpoint SHALL return 409.

#### Scenario: Double-submit of extract-entities

- **WHEN** `extract-entities` is requested for a book already queued/running
- **THEN** the second request gets 409 and no duplicate job is enqueued

### Requirement: Destructive re-extraction SHALL keep a backup

A `force=true` re-extraction SHALL copy the existing canonical to a rolling `.bak` before overwriting, and extraction sidecar files SHALL be written atomically (tmp+rename).

#### Scenario: Force overwrite preserves the prior canonical

- **WHEN** `extract-entities?force=true` runs over an existing canonical
- **THEN** the pre-run file is available as `<slug>.json.bak` after the overwrite

