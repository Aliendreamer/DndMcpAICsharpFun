# embedding-vector-store

## Purpose

Defines the requirements for embedding content chunks via Ollama and upserting them into the Qdrant vector store, including batching behaviour and deterministic point ID derivation.

## Requirements

### Requirement: Content chunks are embedded and upserted in batches
The system SHALL embed chunk text in batches (size configured via `Ingestion:EmbeddingBatchSize`), upsert each batch into Qdrant, and process all chunks before returning.

#### Scenario: All chunks are upserted
- **WHEN** `IngestAsync` is called with a list of chunks and a file hash
- **THEN** every chunk is embedded and upserted into Qdrant exactly once

#### Scenario: Batching respects configured batch size
- **WHEN** the chunk count exceeds `EmbeddingBatchSize`
- **THEN** embeddings and upserts are performed in multiple batches, each no larger than the configured size

### Requirement: Each Qdrant point carries the full chunk payload
The system SHALL store the following fields on every Qdrant point: `text`, `source_book`, `version`, `category`, `entity_name` (when present), `chapter`, `page_number`, `chunk_index`.

#### Scenario: Payload fields are written on upsert
- **WHEN** a chunk is upserted into Qdrant
- **THEN** the point payload contains all required fields with correct values derived from the chunk's metadata

### Requirement: Point IDs are deterministic and derived from content
The system SHALL derive each Qdrant point ID as a UUID computed from the SHA-256 hash of `fileHash + chunkIndex`, ensuring that re-ingesting the same file overwrites existing points rather than creating duplicates.

#### Scenario: Same file re-ingested produces identical point IDs
- **WHEN** the same book is ingested twice with the same file hash
- **THEN** each chunk produces the same point ID on both runs, resulting in upserts rather than inserts

### Requirement: The Qdrant collection is initialised on startup
The system SHALL create the Qdrant collection with cosine distance if it does not exist, and create payload indexes on `source_book`, `version`, `category`, `entity_name` (keyword) and `page_number`, `chunk_index` (integer).

#### Scenario: Collection is created when absent
- **WHEN** the application starts and the Qdrant collection does not exist
- **THEN** the system creates the collection with the configured vector size and cosine distance

#### Scenario: Existing collection is left unchanged
- **WHEN** the application starts and the Qdrant collection already exists
- **THEN** the system logs that the collection exists and skips creation
