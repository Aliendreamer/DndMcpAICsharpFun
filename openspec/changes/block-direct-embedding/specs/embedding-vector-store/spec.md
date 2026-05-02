# embedding-vector-store (delta)

## ADDED Requirements

### Requirement: A second Qdrant collection holds block-level points
The system SHALL maintain two Qdrant collections: the existing one (default name `dnd_chunks`) for LLM-extracted chunks and a new one (default name configured by `Qdrant:BlocksCollectionName`, default `dnd_blocks`) for block-level points produced by the no-LLM path. Both collections SHALL share the same vector size and distance metric so that a single embedding model serves both.

#### Scenario: Both collections exist after startup
- **WHEN** the application starts with a fresh Qdrant instance
- **THEN** `QdrantCollectionInitializer` creates both `dnd_chunks` and `dnd_blocks` (or whatever names are configured), each with the same vector size, the same `Distance.Cosine` metric, and the same payload-field indexes

#### Scenario: Initialization is idempotent for both collections
- **WHEN** the application restarts and both collections already exist
- **THEN** initialisation logs that they exist and does not recreate them or alter their schemas

### Requirement: The blocks collection has the same payload-field indexes
The blocks collection SHALL have the same payload-field indexes as the chunks collection (keyword: `source_book`, `version`, `category`, `section_title`, `entity_name`; integer: `page_number`, `chunk_index`, `page_end`, `section_start`, `section_end`) plus an additional integer index on `block_order` to allow stable ordering of blocks from the same page in retrieval if ever required.

#### Scenario: block_order is queryable on the blocks collection
- **WHEN** a Qdrant search is executed against the blocks collection with a `block_order` filter
- **THEN** the filter applies efficiently because the index is present
