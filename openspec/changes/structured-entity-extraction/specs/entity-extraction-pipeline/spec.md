## ADDED Requirements

### Requirement: Entity extraction SHALL be triggered out-of-band via an admin endpoint

The system SHALL expose `POST /admin/books/{id}/extract-entities` which enqueues a one-time entity extraction job for the specified book. The extraction job SHALL NOT run as part of `POST /admin/books/{id}/ingest-blocks`. The handler SHALL return HTTP 202 on enqueue, HTTP 404 when the record is missing, and HTTP 409 when the record's status indicates an extraction or ingestion is already in progress.

#### Scenario: Valid book is enqueued for extraction
- **WHEN** `POST /admin/books/{id}/extract-entities` is called for an existing book whose status is not `Processing`
- **THEN** the system enqueues an entity-extraction work item and returns HTTP 202

#### Scenario: Unknown book returns 404
- **WHEN** the request specifies an id that does not correspond to any record
- **THEN** the system returns HTTP 404 without enqueuing work

#### Scenario: Already-processing book returns 409
- **WHEN** the targeted record's status is `Processing`
- **THEN** the system returns HTTP 409 without enqueuing work

### Requirement: Extraction SHALL produce canonical JSON written to `data/canonical/<book-slug>.json`

The extraction worker SHALL produce a canonical JSON file at `data/canonical/<book-slug>.json` containing the schemaVersion field, a `book` metadata block (display name, source path, edition, hash), and an `entities[]` array of records each conforming to the structured-entities envelope.

#### Scenario: Successful extraction writes canonical JSON
- **WHEN** entity extraction completes for a book
- **THEN** a file is written at `data/canonical/<book-slug>.json` whose contents validate against the canonical-JSON schema and contain at least one entity

#### Scenario: Re-running extraction overwrites the file deterministically
- **WHEN** entity extraction is run twice on the same unchanged book with the same prompt and model
- **THEN** the resulting JSON file is byte-identical (or differs only in declared timestamps) and re-extraction does not duplicate entities

### Requirement: Extraction SHALL consume Docling-extracted blocks, not re-run layout analysis

The extraction worker SHALL reuse the same Docling output that block ingestion uses. It SHALL NOT issue a fresh Docling layout-analysis request for a book that has already been block-extracted in the same session.

#### Scenario: Docling output is reused when available
- **WHEN** entity extraction runs against a book whose Docling output is already cached or persisted from a recent block ingestion
- **THEN** the extractor reads from the cache rather than calling docling-serve

#### Scenario: Docling unavailability fails extraction with a clear error
- **WHEN** Docling output is needed but neither cached nor obtainable
- **THEN** the worker marks the extraction failed with a clear error identifying the missing layout output

### Requirement: LLM extraction SHALL be schema-constrained per entity type

The extraction worker SHALL invoke the LLM separately for each candidate entity (or chunk of related text), passing the per-type JSON schema as a constraint. The LLM output SHALL be validated against the schema before being added to the canonical JSON. Records that fail validation after a bounded retry budget SHALL be written to a `data/canonical/<book-slug>.errors.json` file for human review and SHALL NOT appear in the canonical JSON.

#### Scenario: Schema-conforming LLM output is added to canonical JSON
- **WHEN** the LLM returns JSON that validates against the per-type schema
- **THEN** the record is added to the canonical JSON's `entities[]` array

#### Scenario: Schema-violating LLM output is recorded in errors file
- **WHEN** the LLM returns JSON that does not validate against the schema after retries
- **THEN** the offending output and validation errors are appended to `data/canonical/<book-slug>.errors.json` and the canonical JSON is unaffected

### Requirement: Extraction SHALL emit progress and a final summary

The worker SHALL log structured progress events (entities-extracted, current type, current page) at a regular cadence and SHALL produce a final summary log entry stating total entities extracted, count per type, and count of validation failures.

#### Scenario: Progress is logged during extraction
- **WHEN** extraction is running
- **THEN** a structured log event is emitted at least every 60 seconds containing current counts and progress percentage

#### Scenario: Final summary is logged on completion
- **WHEN** extraction completes (success or failure)
- **THEN** a single summary log entry includes total entities, per-type counts, validation-failure count, and total elapsed time

### Requirement: Cross-entity reference resolution SHALL run after extraction

After all entities are extracted, the worker SHALL run a reference-resolution pass that validates every cross-entity reference (e.g. Class.subclasses[] → Subclass IDs). Dangling references SHALL be logged as warnings and SHALL NOT block the JSON from being written.

#### Scenario: All references resolve cleanly
- **WHEN** every cross-entity reference matches a record in the same canonical JSON or a previously-loaded one
- **THEN** the reference-resolution pass logs zero warnings

#### Scenario: Dangling reference produces a warning
- **WHEN** a Class.subclasses[] entry references a Subclass ID not present in the canonical JSON
- **THEN** a warning is logged identifying the source entity, the field path, and the missing target ID; the canonical JSON is still written

### Requirement: Re-extraction SHALL be idempotent and explicit

Re-running extraction on a book SHALL replace the existing canonical JSON file in full. The system SHALL NOT silently merge new extraction output with hand-corrections in the existing file. The handler SHALL require an explicit `?force=true` query parameter when a canonical JSON already exists for the target book; without it the request SHALL return HTTP 409.

#### Scenario: Re-extraction without force flag is rejected
- **WHEN** `POST /admin/books/{id}/extract-entities` is called for a book that already has a canonical JSON file and `?force=true` is not set
- **THEN** the system returns HTTP 409 and does not enqueue work

#### Scenario: Force re-extraction replaces the canonical JSON
- **WHEN** `POST /admin/books/{id}/extract-entities?force=true` is called
- **THEN** the worker proceeds, fully overwrites the canonical JSON on success, and any prior hand-corrections are lost (the user is responsible for committing them to git first)

### Requirement: Extraction failures SHALL leave the system in a consistent state

If extraction fails for any reason, the canonical JSON file SHALL not be partially written. The system SHALL write to a temp file and rename atomically only on successful completion of the full extraction pass.

#### Scenario: Mid-extraction crash leaves no partial JSON
- **WHEN** extraction crashes or is cancelled before completion
- **THEN** no `data/canonical/<book-slug>.json` file is written, and any temporary files are cleaned up

#### Scenario: Atomic rename on success
- **WHEN** extraction completes successfully
- **THEN** the canonical JSON appears at its final path via a single atomic rename from the temp file
