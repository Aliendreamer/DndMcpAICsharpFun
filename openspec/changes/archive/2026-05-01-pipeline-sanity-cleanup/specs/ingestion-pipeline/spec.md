## MODIFIED Requirements

### Requirement: ExtractBookAsync cleans up existing data before re-extracting
The system SHALL delete any existing Qdrant vectors and JSON files for a book before starting extraction, when the book has previously been extracted or ingested.

#### Scenario: Re-extract a JsonIngested book
- **WHEN** `ExtractBookAsync` is called for a book with status `JsonIngested`
- **THEN** the system deletes the existing Qdrant vectors, deletes the JSON files, resets the book status to `Pending`, and proceeds with fresh extraction

#### Scenario: Re-extract an Extracted book
- **WHEN** `ExtractBookAsync` is called for a book with status `Extracted`
- **THEN** the system deletes the existing JSON files, resets the book status to `Pending`, and proceeds with fresh extraction

#### Scenario: Extract a Pending or Failed book
- **WHEN** `ExtractBookAsync` is called for a book with status `Pending` or `Failed`
- **THEN** the system proceeds with extraction without any cleanup step

## REMOVED Requirements

### Requirement: The system SHALL support a pattern-based chunking ingestion path via POST /admin/books/{id}/reingest
**Reason:** The LLM extraction pipeline (Extract + IngestJson) produces higher-quality structured embeddings and fully supersedes the old chunking approach. Maintaining two pipelines adds complexity with no benefit.
**Migration:** Use `POST /admin/books/{id}/extract` followed by `POST /admin/books/{id}/ingest-json` instead. Books previously ingested via `/reingest` should be re-extracted using the new pipeline.
