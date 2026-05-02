# block-ingestion

## ADDED Requirements

### Requirement: A registered book can be ingested directly as embedded blocks
The system SHALL expose `POST /admin/books/{id}/ingest-blocks` that enqueues a no-LLM ingestion run for the given book. The handler SHALL return HTTP 202 with the record's URL on a successful enqueue, HTTP 404 if the record does not exist, and HTTP 409 if the record's status is `Processing`.

#### Scenario: Valid record is enqueued for block ingestion
- **WHEN** a `POST /admin/books/{id}/ingest-blocks` request is sent for an existing record whose status is not `Processing`
- **THEN** the system enqueues an `IngestionWorkItem` of type `IngestBlocks` for that record id and returns HTTP 202 with `Location: /admin/books/{id}`

#### Scenario: Unknown record returns 404
- **WHEN** the request specifies an id that does not correspond to any record
- **THEN** the system returns HTTP 404 without enqueuing work

#### Scenario: Already-processing record returns 409
- **WHEN** the targeted record's status is `Processing`
- **THEN** the system returns HTTP 409 without enqueuing work

### Requirement: Block ingestion is end-to-end without LLM extraction
The block-ingestion worker SHALL, for each book: read the PDF's bookmarks, build a `TocCategoryMap` via `BookmarkTocMapper`, call `IPdfBlockExtractor.ExtractBlocks` for the file, attach bookmark-derived `(section_title, category, section_start, section_end)` metadata to each block based on its `PageNumber`, embed the block text via the existing `IEmbeddingService`, and upsert all resulting points into the blocks collection in Qdrant. The worker SHALL NOT call any LLM entity extractor and SHALL NOT write JSON intermediate files to disk.

#### Scenario: A bookmarked book is fully ingested
- **WHEN** block ingestion runs against a registered book whose PDF has bookmarks
- **THEN** every block whose page falls within a section is embedded and upserted; the record is marked `JsonIngested` with the total point count; no `*.json` files are created under `books/extracted/{id}`; and no calls are made to `ILlmEntityExtractor`

#### Scenario: A book without bookmarks fails with a clear error
- **WHEN** block ingestion runs against a book whose bookmark tree is empty or absent
- **THEN** the worker marks the record `Failed` with an error message indicating that bookmark-driven block ingestion requires embedded bookmarks, and writes nothing to Qdrant

#### Scenario: Blocks on pages outside any bookmark section are skipped
- **WHEN** a block's `PageNumber` is outside every section's `(StartPage, EndPage)` range
- **THEN** the block SHALL be skipped (no Qdrant point produced), without failing the run

### Requirement: Each block produces one Qdrant point with section-aware payload
The block-ingestion worker SHALL upsert one point per non-skipped block. Each point's payload SHALL include at least: `text` (the block's text), `source_book`, `version`, `category`, `section_title`, `section_start`, `section_end`, `page_number`, `block_order`. The point id SHALL be a deterministic GUID derived from the file hash and a stable block index so re-runs overwrite without producing duplicates.

#### Scenario: Re-running block ingestion overwrites prior points for the same book
- **WHEN** block ingestion is invoked twice for the same record without changing the file
- **THEN** the second run upserts points with identical ids and the collection's point count for that file hash does not double

#### Scenario: Re-running with a changed file hash replaces old points
- **WHEN** block ingestion is invoked, completes, and is then invoked again after the underlying file has changed (different hash)
- **THEN** the worker first deletes points associated with the old hash and then upserts the new points
