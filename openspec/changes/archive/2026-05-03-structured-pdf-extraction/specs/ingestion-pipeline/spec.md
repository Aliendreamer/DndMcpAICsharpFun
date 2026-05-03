## MODIFIED Requirements

### Requirement: The ingestion orchestrator processes a book end-to-end
The system SHALL compute the file hash as the first step of ingestion, persist it immediately, reuse it for duplicate detection and unchanged-file checks, then extract structured pages using `IPdfStructuredExtractor`, classify and extract entities per page via the LLM, save enriched page JSON, embed and upsert chunks into the vector store, and mark the record `Completed`.

#### Scenario: Book is ingested successfully
- **WHEN** `IngestBookAsync` is called for a `Pending` record
- **THEN** the system computes and stores the hash, extracts structured pages, produces enriched JSON per page, embeds entity descriptions, upserts to Qdrant, and sets status to `Completed` with a chunk count

#### Scenario: Unchanged book is skipped
- **WHEN** `IngestBookAsync` is called for a `Completed` record whose file hash has not changed
- **THEN** the system skips processing and leaves the record unchanged

#### Scenario: Ingestion failure marks the record as Failed
- **WHEN** an unhandled exception occurs during ingestion
- **THEN** the system sets the record status to `Failed` and stores the error message

## ADDED Requirements

### Requirement: Merge pass tracks PageEnd across partial entity chains
The system SHALL set `PageEnd` on each merged entity to the last page number in its partial chain. For entities contained on a single page, `PageEnd` SHALL be null. The chapter boundary from the TOC acts as a hard cap — `PageEnd` SHALL not exceed the end page of the chapter containing the entity's start page.

#### Scenario: Two-page entity records correct PageEnd
- **WHEN** entity X on page 40 has `partial: true` and its continuation on page 41 has `partial: false`
- **THEN** the merged entity SHALL have `PageEnd = 41`

#### Scenario: Single-page entity has null PageEnd
- **WHEN** an entity exists on one page with `partial: false`
- **THEN** its `PageEnd` SHALL be null

#### Scenario: PageEnd is capped at chapter boundary
- **WHEN** a partial chain would extend beyond the TOC chapter end page
- **THEN** `PageEnd` SHALL be set to the chapter end page
