# ingestion-pipeline

## Purpose

Defines the requirements for registering D&D source PDFs, ingesting them into the Qdrant vector store via the no-LLM block path, and deleting them. Ingestion is a single stage: register → ingest-blocks → done. The legacy LLM-driven extract → JSON → embed pipeline has been removed (see `archive/2026-05-03-remove-llm-ingestion-path/`).
## Requirements
### Requirement: A PDF book can be registered via the admin API
The system SHALL accept a PDF upload at `POST /admin/books/register` with form fields `version`, `displayName`, and an optional `bookType` (one of `Core`, `Supplement`, `Adventure`, `Setting`, `Unknown` — case-insensitive; missing or unparseable values default to `Unknown` without HTTP 400). The handler SHALL persist an ingestion record carrying these fields, stream the multipart body directly to disk (no double-buffering), and store the file under a server-generated GUID name; the user-supplied filename is retained only as a sanitised display value.

#### Scenario: Valid PDF is registered successfully
- **WHEN** a PDF file is uploaded with valid `version` and `displayName`
- **THEN** the system stores the file under `{BooksPath}/{guid}.pdf`, creates an `IngestionRecord` with status `Pending`, and returns HTTP 202

#### Scenario: Non-PDF file is rejected
- **WHEN** a file that does not have a `.pdf` extension is uploaded
- **THEN** the system returns HTTP 400 and the partially uploaded file (if any) is deleted

#### Scenario: Invalid version value is rejected
- **WHEN** the `version` field does not match a valid `DndVersion` enum name
- **THEN** the system returns HTTP 400 with a message listing valid values

#### Scenario: Uploaded filename is sanitised against path traversal
- **WHEN** an upload is submitted with a filename containing directory traversal sequences (e.g. `../../etc/passwd`)
- **THEN** the system strips directory components from the display name and never uses any user-controlled string as part of the on-disk path

### Requirement: All registered books can be listed
The system SHALL return all ingestion records at `GET /admin/books`.

#### Scenario: Books list is returned
- **WHEN** `GET /admin/books` is called
- **THEN** the system returns HTTP 200 with a JSON array of all `IngestionRecord` objects

### Requirement: A registered book is ingested as embedded blocks
The system SHALL expose `POST /admin/books/{id}/ingest-blocks` which enqueues a single-stage ingestion run that reads the PDF's bookmark tree, extracts layout-aware blocks via Docstrum, embeds each block's text via the configured embedding model, and upserts the resulting points into the blocks collection in Qdrant. The handler SHALL return HTTP 202 on enqueue, HTTP 404 when the record is missing, and HTTP 409 when the record's status is `Processing`.

#### Scenario: Valid record is enqueued
- **WHEN** `POST /admin/books/{id}/ingest-blocks` is called for an existing record whose status is not `Processing`
- **THEN** the system enqueues an `IngestionWorkItem(Type: IngestBlocks, BookId: id)` and returns HTTP 202 with `Location: /admin/books/{id}`

#### Scenario: Unknown record returns 404
- **WHEN** the request specifies an id that does not correspond to any record
- **THEN** the system returns HTTP 404 without enqueuing work

#### Scenario: Already-processing record returns 409
- **WHEN** the targeted record's status is `Processing`
- **THEN** the system returns HTTP 409 without enqueuing work

### Requirement: Section discovery uses the PDF bookmark tree
During ingestion the system SHALL read the PDF's embedded bookmark tree via `IPdfBookmarkReader.ReadBookmarks`, walk every node recursively, skip nodes whose title is shorter than three trimmed characters, and convert each remaining `(title, pageNumber)` pair into a `TocSectionEntry` with a category guessed from the title. End pages are derived from each subsequent entry's start page minus one (the last entry's end page is open-ended).

#### Scenario: A bookmarked PDF produces a populated section map
- **WHEN** `IngestBlocksAsync` runs against a PDF that has an embedded bookmark tree
- **THEN** the orchestrator builds a `TocCategoryMap` from the bookmarks and uses it to assign each block a section before embedding

#### Scenario: A PDF without bookmarks fails ingestion with a clear error
- **WHEN** `IngestBlocksAsync` runs against a PDF whose bookmark tree is empty or absent
- **THEN** the orchestrator marks the record `Failed` with an error message indicating that bookmark-driven block ingestion requires embedded bookmarks, and writes nothing to Qdrant

#### Scenario: Single-letter divider bookmarks are skipped
- **WHEN** the bookmark tree contains alphabetical-divider entries with titles like `"A"`, `"M"`, `"F"` (length < 3 after trim)
- **THEN** those nodes SHALL not produce `TocSectionEntry` rows; their children are still walked, and pages they covered fall through to the nearest meaningful parent bookmark

#### Scenario: Re-running ingest-blocks overwrites prior block points
- **WHEN** `POST /admin/books/{id}/ingest-blocks` is issued twice in succession on the same book
- **THEN** the second run deletes points associated with the previous file hash before upserting the new ones, leaving no duplicates and no orphans

### Requirement: A book can be deleted via the admin API
The system SHALL expose `DELETE /admin/books/{id}` which removes the SQLite record, deletes the underlying PDF file from disk, and (when the record had a populated `ChunkCount`) deletes the associated points from the Qdrant blocks collection.

#### Scenario: Existing book is deleted
- **WHEN** `DELETE /admin/books/{id}` is called for a record not in `Processing` status
- **THEN** the system removes the Qdrant points for its file hash, deletes the PDF file, deletes the SQLite row, and returns HTTP 204

#### Scenario: Unknown book returns 404
- **WHEN** the id does not match any record
- **THEN** the system returns HTTP 404 and performs no deletion

#### Scenario: Processing book returns 409
- **WHEN** the targeted record's status is `Processing`
- **THEN** the system returns HTTP 409 and performs no deletion

### Requirement: The IngestionQueueWorker dispatches block ingestion
The system SHALL dispatch every queued `IngestionWorkItem` to `IBlockIngestionOrchestrator.IngestBlocksAsync`. There is exactly one work-item type (`IngestBlocks`); the worker has no other branches.

#### Scenario: Queue dispatches IngestBlocks items to the block orchestrator
- **WHEN** an `IngestionWorkItem(IngestBlocks, BookId: 5)` is dequeued
- **THEN** the worker invokes `IBlockIngestionOrchestrator.IngestBlocksAsync(5, ...)`

### Requirement: Embedding inputs are pre-truncated to fit the model context
The system SHALL pre-truncate every text passed to the embedding model to at most 1500 characters before issuing the embed request, to protect against the embedding model's context-length limit. Inputs that are truncated SHALL be logged at Warning level.

#### Scenario: Long block text is truncated before embedding
- **WHEN** the block ingestion path embeds a block whose text exceeds 1500 characters
- **THEN** the embedding service truncates the input to 1500 characters and logs a warning, and the embed call succeeds without a context-length error

### Requirement: Block ingestion filters fragments and numeric runs
The system SHALL skip blocks shorter than 40 characters and blocks whose letter content is below 40% of total characters (treating digits, punctuation, and whitespace as non-letters). This removes single-word fragments and standalone numeric tables (e.g. spell-slot rows) that would otherwise pollute retrieval.

#### Scenario: A 7-character fragment is skipped
- **WHEN** a Docstrum block produces text `"god lhe"`
- **THEN** the orchestrator skips it before embedding

#### Scenario: A pure-numeric table row is skipped
- **WHEN** a Docstrum block produces text `"19 4 3 3 3 2 1 1"` (no letters)
- **THEN** the orchestrator skips it before embedding

#### Scenario: A normal sentence with numbers is kept
- **WHEN** a block produces `"Fireball deals 8d6 fire damage to creatures within a 20-foot radius."`
- **THEN** the orchestrator keeps it (letters are well above 40% of total characters)

### Requirement: Ingestion options expose the page-segmenter knob
The `IngestionOptions` configuration class SHALL expose a `BlockSegmenter` property (string, default `"docstrum"`). The value is consumed by `PdfPigBlockExtractor` to choose between `DocstrumBoundingBoxes` and `RecursiveXYCut`. No other ingestion behaviour is affected by this knob.

#### Scenario: Default value preserves existing behaviour
- **WHEN** the application starts with no `Ingestion:BlockSegmenter` value in any configuration source
- **THEN** ingestion proceeds exactly as it did before this change, using `DocstrumBoundingBoxes`

### Requirement: Docling is reachable when block ingestion uses it
When `Ingestion:BlockSegmenter` is `"docling"`, the system SHALL require docling-serve to be reachable at `Docling:BaseUrl`. If docling-serve is unhealthy or unreachable at the time `IngestBlocksAsync` runs, the orchestrator SHALL mark the record `Failed` with an error message identifying docling-serve as the failing dependency, and SHALL NOT write any points to Qdrant.

#### Scenario: Docling unreachable during ingestion fails the record cleanly
- **WHEN** block ingestion runs with `BlockSegmenter=docling` and docling-serve is down
- **THEN** the orchestrator marks the record `Failed` with an error message that names docling-serve, the configured `Docling:BaseUrl`, and the underlying HTTP error

#### Scenario: Health check surfaces docling outages
- **WHEN** the application's `/ready` endpoint is hit while docling-serve is down
- **THEN** the response includes a failed `docling` health-check entry, regardless of whether the running ingestion mode actually uses docling

