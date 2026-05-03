# embedding-vector-store

## Purpose

Defines the requirements for embedding block text via Ollama and upserting it into the Qdrant vector store, including batching behaviour, payload schema, and deterministic point ID derivation. There is exactly one active collection: `dnd_blocks`. The legacy `dnd_chunks` collection is no longer created or written to.

## Requirements

### Requirement: Block chunks are embedded and upserted in batches
The system SHALL embed block text in batches (default size 32, configurable via `Ingestion:EmbeddingBatchSize`), upsert each batch into Qdrant's blocks collection, and process all chunks before returning.

#### Scenario: All chunks are upserted
- **WHEN** `IngestBlocksAsync` produces a list of block chunks for a given file hash
- **THEN** every chunk is embedded and upserted into Qdrant exactly once

#### Scenario: Batching respects configured batch size
- **WHEN** the chunk count exceeds the embedding batch size
- **THEN** embeddings and upserts are performed in multiple batches, each no larger than the configured size

### Requirement: Each Qdrant point carries the full block payload
The system SHALL store the following fields on every Qdrant point: `text`, `source_book`, `version`, `category`, `section_title`, `section_start`, `section_end`, `page_number`, `block_order`, `chunk_index`, `book_type`.

#### Scenario: Payload fields are written on upsert
- **WHEN** a block is upserted into Qdrant
- **THEN** the point payload contains all required fields with values derived from the block's metadata

### Requirement: Point IDs are deterministic and derived from content
The system SHALL derive each Qdrant point ID as a UUID computed from the SHA-256 hash of `fileHash + globalIndex`, ensuring that re-ingesting the same file overwrites existing points rather than creating duplicates.

#### Scenario: Same file re-ingested produces identical point IDs
- **WHEN** the same book is ingested twice with the same file hash and the same blocks emerge in the same order
- **THEN** each block produces the same point ID on both runs, resulting in upserts rather than inserts

### Requirement: The Qdrant blocks collection is initialised on startup
The system SHALL create the Qdrant collection named by `Qdrant:BlocksCollectionName` (default `dnd_blocks`) with cosine distance and the configured vector size if it does not exist, and SHALL create payload indexes on `source_book`, `version`, `category`, `entity_name`, `section_title`, `book_type` (keyword) and `page_number`, `chunk_index`, `page_end`, `section_start`, `section_end`, `block_order` (integer). Initialisation SHALL retry on transient failures.

#### Scenario: Collection is created when absent
- **WHEN** the application starts and the blocks collection does not exist
- **THEN** the system creates the collection with the configured vector size, cosine distance, and the standard payload indexes

#### Scenario: Existing collection is left unchanged
- **WHEN** the application starts and the blocks collection already exists
- **THEN** the system logs that the collection exists and skips creation

#### Scenario: Initialiser does not create the legacy chunks collection
- **WHEN** the application starts on a fresh Qdrant volume
- **THEN** only `dnd_blocks` is created; the legacy `dnd_chunks` collection is not created
