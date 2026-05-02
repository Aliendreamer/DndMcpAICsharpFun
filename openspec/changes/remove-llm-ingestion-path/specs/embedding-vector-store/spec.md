# embedding-vector-store (delta)

## REMOVED Requirements

### Requirement: A Qdrant collection holds LLM-extracted chunks
**Reason**: The `dnd_chunks` collection only received writes from the LLM-extracted JSON ingestion pipeline that is being removed. No producer remains.
**Migration**: `QdrantCollectionInitializer` no longer issues `CreateCollectionAsync` for `dnd_chunks`. Existing collections in deployed Qdrant volumes are left in place (orphaned). Operators reclaim disk with `curl -X DELETE http://qdrant:6333/collections/dnd_chunks`.

## MODIFIED Requirements

### Requirement: Qdrant initialiser ensures the configured collection on startup
The system SHALL ensure the Qdrant collection named by `Qdrant:BlocksCollectionName` (default `dnd_blocks`) exists at startup, with vector size `Qdrant:VectorSize`, distance `Cosine`, and the standard payload-field indexes. Initialisation SHALL retry on transient failures and SHALL be idempotent on subsequent restarts.

#### Scenario: Fresh deployment creates the blocks collection
- **WHEN** the application starts against a Qdrant instance that has no `dnd_blocks` collection
- **THEN** the initialiser creates `dnd_blocks` with the configured vector size and the standard indexes (keyword: source_book, version, category, entity_name, section_title; integer: page_number, chunk_index, page_end, section_start, section_end, block_order)

#### Scenario: Restart on a pre-existing collection is a no-op
- **WHEN** the application restarts and `dnd_blocks` already exists
- **THEN** the initialiser logs that the collection exists and does not recreate it or alter its schema

#### Scenario: Initialiser does not create dnd_chunks
- **WHEN** the application starts on a fresh Qdrant volume
- **THEN** only `dnd_blocks` is created; the legacy `dnd_chunks` collection is not created
