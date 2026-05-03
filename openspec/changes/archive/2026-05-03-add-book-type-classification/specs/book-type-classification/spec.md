# book-type-classification

## ADDED Requirements

### Requirement: BookType is a five-value enum
The system SHALL define `BookType` as an enum in the `DndMcpAICsharpFun.Domain` namespace with exactly the values `Core`, `Supplement`, `Adventure`, `Setting`, and `Unknown`. `Unknown` SHALL be the default value (zero ordinal). The five values together describe how WotC publishes 5e+ source material and are intended to compose with the existing `version` (rules edition) and `source_book` (display name) fields.

#### Scenario: BookType has the documented members
- **WHEN** the BookType enum is reflected
- **THEN** it contains exactly the values listed above and `Unknown` is the default value of the type

### Requirement: Books carry a BookType from registration through retrieval
Every `IngestionRecord` SHALL store a `BookType` value. Every Qdrant block point produced for that record SHALL carry the same `book_type` keyword payload field. Retrieval results SHALL surface the BookType in their metadata so consuming agents can see the classification of each result.

#### Scenario: A registered book stores its BookType in SQLite
- **WHEN** a book is registered with `bookType=Core`
- **THEN** the resulting `IngestionRecord` has `BookType == BookType.Core` and the value persists across restarts

#### Scenario: Block points carry the BookType in their payload
- **WHEN** that registered book is ingested via `/ingest-blocks`
- **THEN** every Qdrant point upserted for it has `payload.book_type == "Core"`

#### Scenario: Retrieval results expose the BookType
- **WHEN** a search hits a point upserted from a Core-typed book
- **THEN** the result's `metadata.bookType` is `Core`

### Requirement: Unset BookType reads as Unknown
Pre-existing rows in `IngestionRecords` and pre-existing points in `dnd_blocks` SHALL be readable after this change. Where the new field is absent or null, it SHALL be surfaced as `BookType.Unknown` rather than throwing or silently dropping the row.

#### Scenario: Pre-existing IngestionRecord rows expose Unknown
- **WHEN** a record was inserted before this change and never had a value written for the column
- **THEN** loading it through `IIngestionTracker` yields `BookType.Unknown`

#### Scenario: Pre-existing Qdrant points expose Unknown
- **WHEN** a Qdrant point lacks the `book_type` payload field
- **THEN** `QdrantPayloadMapper.ToChunkMetadata` returns `ChunkMetadata` with `BookType.Unknown`

#### Scenario: Filter for Unknown matches pre-existing points
- **WHEN** retrieval is queried with `?bookType=Unknown`
- **THEN** pre-existing points whose payload lacks the field are returned (because `Unknown` is the implicit value)
