## ADDED Requirements

### Requirement: Qdrant collection is created on startup if it does not exist
The system SHALL check for the existence of the `dnd_chunks` collection on startup and create it with cosine distance and the configured vector size if absent.

#### Scenario: First startup creates the collection
- **WHEN** the application starts and `dnd_chunks` does not exist in Qdrant
- **THEN** the collection is created with vector size from `QdrantOptions:VectorSize` and cosine distance

#### Scenario: Subsequent startup leaves existing collection intact
- **WHEN** the application starts and `dnd_chunks` already exists
- **THEN** no modification is made to the existing collection

### Requirement: All ChunkMetadata fields are stored as indexed Qdrant payload
The system SHALL store `source_book`, `version`, `category`, `entity_name`, `chapter`, `page_number`, and `chunk_index` as Qdrant point payload, with keyword indexes on `source_book`, `version`, `category`, and `entity_name`.

#### Scenario: Upserted point has correct payload
- **WHEN** a chunk with metadata `{Category: "spell", Version: "2024", EntityName: "Fireball"}` is upserted
- **THEN** the corresponding Qdrant point payload contains `category: "spell"`, `version: "2024"`, `entity_name: "Fireball"`

### Requirement: Point IDs are deterministic from file hash and chunk index
The system SHALL derive each point's Qdrant UUID deterministically from a combination of the ingestion record's file hash and the chunk's index within that file.

#### Scenario: Re-ingesting the same file produces the same point IDs
- **WHEN** the same book is ingested twice
- **THEN** the upsert overwrites existing points rather than creating duplicates

### Requirement: Qdrant upsert is performed in batches matching the embedding batch size
The system SHALL upsert points to Qdrant in the same batches used for embedding, not one point at a time.

#### Scenario: Batch upsert is called once per embedding batch
- **WHEN** 32 chunks are embedded in one batch
- **THEN** one Qdrant upsert call is made for those 32 points
