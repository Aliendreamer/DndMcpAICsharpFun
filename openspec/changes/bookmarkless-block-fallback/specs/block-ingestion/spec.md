## MODIFIED Requirements

### Requirement: Block ingestion is end-to-end without LLM extraction
The block-ingestion worker SHALL, for each book: read the PDF's bookmarks and build a `TocCategoryMap` via `BookmarkTocMapper`; when the bookmark tree is empty or absent, instead build a full-coverage `TocCategoryMap` from the block extractor's `section_header` items via `FullCoverageHeadingTocMapper` (see the `full-coverage-block-toc` capability). It SHALL then call the block extractor for the file, attach the resulting `(section_title, category, section_start, section_end)` metadata to each block based on its `PageNumber`, embed the block text via the existing `IEmbeddingService`, and upsert all resulting points into the blocks collection in Qdrant. The worker SHALL NOT call any LLM entity extractor and SHALL NOT write JSON intermediate files to disk.

#### Scenario: A bookmarked book is fully ingested

- **WHEN** block ingestion runs against a registered book whose PDF has bookmarks
- **THEN** the bookmark path is used unchanged; every block whose page falls within a section is embedded and upserted; the record is marked `JsonIngested` with the total point count; no `*.json` files are created under `books/extracted/{id}`; and no calls are made to `ILlmEntityExtractor`

#### Scenario: A book without bookmarks falls back to a full-coverage heading TOC

- **WHEN** block ingestion runs against a book whose bookmark tree is empty or absent but whose conversion yields section-header items
- **THEN** the worker builds a full-coverage TOC from those headings, every prose block resolves to a section (nothing dropped), and the record is marked `JsonIngested` with the total point count

#### Scenario: A book with neither bookmarks nor prose fails with a clear error

- **WHEN** block ingestion runs against a book that produces no prose blocks at all (after the heading-fallback still yields zero embeddable chunks)
- **THEN** the worker marks the record `Failed` with an error message indicating no ingestable content was found, and writes nothing to Qdrant

#### Scenario: Blocks on pages outside any bookmark section are skipped

- **WHEN** a block's `PageNumber` is outside every section's `(StartPage, EndPage)` range
- **THEN** the block SHALL be skipped (no Qdrant point produced), without failing the run
