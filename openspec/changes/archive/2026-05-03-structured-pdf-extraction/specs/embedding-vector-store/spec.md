## MODIFIED Requirements

### Requirement: Each Qdrant point carries the full chunk payload
The system SHALL store the following fields on every Qdrant point: `text`, `source_book`, `version`, `category`, `entity_name` (when present), `chapter`, `page_number`, `chunk_index`, and `page_end` (integer, omitted when null).

#### Scenario: Payload fields are written on upsert
- **WHEN** a chunk is upserted into Qdrant
- **THEN** the point payload SHALL contain all required fields with correct values derived from the chunk's metadata

#### Scenario: page_end is included for multi-page entities
- **WHEN** a chunk has a non-null `PageEnd` value
- **THEN** the Qdrant point payload SHALL include `page_end` set to that value

#### Scenario: page_end is omitted for single-page entities
- **WHEN** a chunk has a null `PageEnd` value
- **THEN** the Qdrant point payload SHALL not include a `page_end` field

### Requirement: The Qdrant collection is initialised on startup
The system SHALL create the Qdrant collection with cosine distance and vector size 1024 if it does not exist, and create payload indexes on `source_book`, `version`, `category`, `entity_name` (keyword) and `page_number`, `chunk_index`, `page_end` (integer).

#### Scenario: Collection is created when absent
- **WHEN** the application starts and the Qdrant collection does not exist
- **THEN** the system creates the collection with vector size 1024 and cosine distance

#### Scenario: Existing collection is left unchanged
- **WHEN** the application starts and the Qdrant collection already exists
- **THEN** the system logs that the collection exists and skips creation
