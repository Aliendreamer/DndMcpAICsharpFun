# entity-extraction-pipeline

## Purpose

Defines the requirements for the out-of-band LLM-driven entity extraction pipeline. Extraction converts Docling-processed book text into canonical JSON files (`data/canonical/<book-slug>.json`) containing structured entity records. Extraction is decoupled from block ingestion and is triggered explicitly via an admin endpoint.

## Requirements

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

### Requirement: Cross-entity reference resolution SHALL distinguish intra-book from inter-book

After candidate entities are produced, the worker SHALL run a reference-resolution pass that classifies every dangling cross-entity reference by whether the target's slug prefix matches the current book's slug prefix.

**Intra-book dangling references** (target slug prefix equals the current book's slug) SHALL be treated as extraction failures: the offending source entity SHALL be excluded from the canonical JSON and recorded in `data/canonical/<book-slug>.errors.json`. The worker MAY retry the affected entities with a corrective prompt up to a bounded retry budget, asking the LLM to either produce the missing target or remove the reference.

**Inter-book dangling references** (target slug prefix differs from the current book's slug) SHALL be recorded in a sibling `data/canonical/<book-slug>.warnings.json` file and SHALL NOT block extraction or affect the canonical JSON.

#### Scenario: All references resolve cleanly

- **WHEN** every cross-entity reference matches a record in the same canonical JSON or has a target outside the current book
- **THEN** the canonical JSON is written and no errors file is produced; the warnings file is either absent or empty

#### Scenario: Intra-book dangling reference excludes the source entity

- **WHEN** a Class entity references a Subclass ID with the same book-slug prefix as the current book and that Subclass entity is not produced after the bounded retry budget
- **THEN** the source Class entity is excluded from the canonical JSON and an entry naming the source entity, the field path, and the missing target ID is appended to `data/canonical/<book-slug>.errors.json`

#### Scenario: Inter-book dangling reference is recorded as a warning

- **WHEN** an entity references an ID whose book-slug prefix differs from the current book's slug
- **THEN** the canonical JSON is written with the entity intact and an entry naming the source entity, the field path, and the missing target ID is appended to `data/canonical/<book-slug>.warnings.json`

### Requirement: Corpus-wide validation endpoint SHALL report cross-book integrity

The system SHALL expose `POST /admin/canonical/validate` which scans every canonical JSON file under `data/canonical/`, loads them through the canonical loader, and runs the cross-entity reference resolver across the union. The endpoint SHALL return HTTP 200 with a structured report when zero FAIL-class issues are found, and HTTP 422 with the same body shape when any FAIL-class issue is present. FAIL-class issues are: schema-version mismatches, duplicate entity IDs across files, and intra-book dangling references that somehow survived extraction. Inter-book dangling references SHALL be reported as warnings (do not contribute to FAIL).

#### Scenario: Clean corpus returns 200 with empty failures

- **WHEN** `POST /admin/canonical/validate` is called against a corpus where every canonical JSON loads cleanly and every cross-entity reference resolves
- **THEN** the response is HTTP 200 with a JSON body whose `failures` array is empty and whose `warnings` array contains any inter-book dangling references

#### Scenario: Schema-version mismatch returns 422

- **WHEN** any canonical JSON file in the corpus has `schemaVersion` other than the loader's `CurrentVersion`
- **THEN** the response is HTTP 422 with a `failures` entry naming the offending file and the schema-version mismatch

#### Scenario: Cross-file duplicate ID returns 422

- **WHEN** two different canonical JSON files contain entities with the same `id`
- **THEN** the response is HTTP 422 with a `failures` entry naming both files and the duplicated id

#### Scenario: Inter-book dangling reference is reported as warning, not failure

- **WHEN** an entity in book A references an ID whose book-slug prefix matches book B but book B's canonical JSON has not yet been ingested into the corpus
- **THEN** the response is HTTP 200 with a `warnings` entry naming the dangling ref, and `failures` is empty

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
