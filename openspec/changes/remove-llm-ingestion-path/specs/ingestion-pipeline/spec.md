# ingestion-pipeline (delta)

## REMOVED Requirements

### Requirement: A book can be re-ingested on demand
**Reason**: The two-stage extract/ingest-json flow that this requirement implied (`POST /admin/books/{id}/reingest`, or the route's evolution into `/extract` + `/ingest-json`) is removed. Re-running ingestion is now done via `POST /admin/books/{id}/ingest-blocks`, which is idempotent — it deletes prior block points keyed by file hash before upserting the new ones.
**Migration**: Stop calling `/extract`, `/ingest-json`, `/extract-page/{n}`, and `/cancel-extract`. Use `/ingest-blocks` exclusively for the ingestion lifecycle.

### Requirement: The ingestion orchestrator processes a book end-to-end
**Reason**: The `IIngestionOrchestrator.ExtractBookAsync` + `IngestJsonAsync` pair is removed along with the multi-stage pipeline they drove. Block ingestion is single-stage and lives in `IBlockIngestionOrchestrator.IngestBlocksAsync`.
**Migration**: External code that referenced `IIngestionOrchestrator` for ingestion (which is none — it was internal) takes no action. `DeleteBookAsync` is moved to `IBookDeletionService.DeleteBookAsync` with an identical signature; consumers update their DI registrations to inject the new interface.

### Requirement: A background service automatically processes pending and failed books
**Reason**: The 24-hour polling loop existed to retry stuck LLM extractions. With single-stage block ingestion, retries are user-driven by re-issuing `POST /ingest-blocks` rather than auto-retried by a poller. Removing the polling loop also removes a long-running task that competed with user-triggered ingestions for Ollama capacity.
**Migration**: A book stuck in `Failed` status is recovered by re-issuing `POST /admin/books/{id}/ingest-blocks` once the operator has fixed whatever caused the failure (missing bookmarks, network, etc.).

## MODIFIED Requirements

### Requirement: Section discovery uses the PDF bookmark tree
During ingestion, the system SHALL read the PDF's embedded bookmark tree via `IPdfBookmarkReader.ReadBookmarks`, walk every node recursively, skip nodes whose title is shorter than three trimmed characters, and convert each remaining `(title, pageNumber)` pair to a `TocSectionEntry`. End pages SHALL be derived from each subsequent entry's start page minus one (the last entry's end page is open-ended). This requirement applies to the block-ingestion path; no other ingestion path exists after this change.

#### Scenario: A bookmarked PDF produces a populated section map
- **WHEN** `IngestBlocksAsync` runs against a PDF that has an embedded bookmark tree
- **THEN** the orchestrator builds a `TocCategoryMap` from the bookmarks and uses it to assign each block a section before embedding

#### Scenario: A PDF without bookmarks fails ingestion with a clear error
- **WHEN** `IngestBlocksAsync` runs against a PDF whose bookmark tree is empty or absent
- **THEN** the orchestrator marks the record `Failed` with an error message indicating that bookmark-driven block ingestion requires embedded bookmarks, and writes nothing to Qdrant

#### Scenario: Single-letter divider bookmarks are skipped
- **WHEN** the bookmark tree contains alphabetical-divider entries with titles like `"A"`, `"B"`, `"M"` (length < 3 after trim)
- **THEN** those nodes SHALL not produce `TocSectionEntry` rows; their children are still walked, and pages they covered fall through to the nearest meaningful parent bookmark

#### Scenario: Re-running ingest-blocks overwrites prior block points
- **WHEN** `POST /admin/books/{id}/ingest-blocks` is issued twice in succession on the same book
- **THEN** the second run deletes points associated with the previous file hash before upserting the new ones, leaving no duplicates and no orphans
