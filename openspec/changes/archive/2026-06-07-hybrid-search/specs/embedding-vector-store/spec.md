## ADDED Requirements
### Requirement: Each Qdrant point carries both dense and sparse vectors
The system SHALL embed block text using the configured Ollama model (dense vector) AND compute a BM25 sparse vector, then upsert both into the `dnd_blocks` collection in a single batch operation. If the collection has no sparse vector support, only the dense vector SHALL be upserted. The existing payload fields (`text`, `source_book`, `version`, `category`, `section_title`, `section_start`, `section_end`, `page_number`, `block_order`, `chunk_index`, `book_type`) SHALL remain unchanged.

#### Scenario: Both vectors upserted when collection supports sparse

- **WHEN** a block is upserted into a collection with sparse vector support
- **THEN** the Qdrant point contains a dense vector and a `text-sparse` sparse vector

#### Scenario: Dense-only upsert when collection lacks sparse support

- **WHEN** a block is upserted into a collection without sparse vector support
- **THEN** only the dense vector is upserted and no error is raised

## MODIFIED Requirements
### Requirement: The Qdrant blocks collection is initialised on startup
The system SHALL create the Qdrant collection named by `Qdrant:BlocksCollectionName` (default `dnd_blocks`) with cosine distance, the configured vector size, and a named sparse vector field `"text-sparse"` if it does not exist. The system SHALL create payload indexes on `source_book`, `version`, `category`, `entity_name`, `section_title`, `book_type` (keyword) and `page_number`, `chunk_index`, `page_end`, `section_start`, `section_end`, `block_order` (integer). If the collection exists without sparse vector support, the system SHALL log a warning and skip sparse vector upserts during ingestion. Initialisation SHALL retry on transient failures.

#### Scenario: Fresh collection created with sparse support

- **WHEN** the application starts on a fresh Qdrant volume
- **THEN** the `dnd_blocks` collection is created with both a dense vector config and a `text-sparse` sparse vector config

#### Scenario: Existing collection without sparse support detected

- **WHEN** the application starts and `dnd_blocks` already exists without a sparse vector named field
- **THEN** a warning is logged, and ingestion proceeds with dense vectors only until the operator re-creates the collection
