# embedding-vector-store (delta)

## MODIFIED Requirements

### Requirement: Each Qdrant point carries the full chunk payload
The system SHALL store the following fields on every Qdrant point: `text`, `source_book`, `version`, `category`, `entity_name`, `chapter`, `page_number`, `chunk_index`, `page_end`, `section_title`, `section_start`, `section_end`.

#### Scenario: Section fields are written on upsert
- **WHEN** a chunk with `SectionTitle="Warlock"`, `SectionStart=105`, `SectionEnd=112` is upserted
- **THEN** the Qdrant point payload contains `section_title="Warlock"`, `section_start=105`, `section_end=112`

#### Scenario: Payload fields are written on upsert
- **WHEN** a chunk is upserted into Qdrant
- **THEN** the point payload contains all required fields with correct values derived from the chunk's metadata

### Requirement: The Qdrant collection is initialised on startup
The system SHALL create the Qdrant collection with cosine distance if it does not exist, and create payload indexes on `source_book`, `version`, `category`, `entity_name`, `section_title` (keyword) and `page_number`, `chunk_index`, `page_end`, `section_start`, `section_end` (integer).

#### Scenario: Collection is created when absent
- **WHEN** the application starts and the Qdrant collection does not exist
- **THEN** the system creates the collection with the configured vector size and cosine distance

#### Scenario: Section indexes are created
- **WHEN** the collection is initialised
- **THEN** keyword indexes exist for `section_title` and integer indexes exist for `section_start` and `section_end`
