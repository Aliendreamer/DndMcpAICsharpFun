# book-type-classification

## Purpose

Defines the `BookType` taxonomy and the contract for tagging registered books and filtering retrieval results by book type. Adds a five-value categorical axis (`Core`, `Supplement`, `Adventure`, `Setting`, `Unknown`) orthogonal to `version` (rules edition) and `source_book` (display name). Together these three fields let retrieval target precise slices of a multi-book corpus — for example "rules-only content from the 2014 PHB", "adventure-specific monster blocks", or "lore from any setting book".

## Requirements

### Requirement: BookType is a five-value enum
The system SHALL define `BookType` as an enum in the `DndMcpAICsharpFun.Domain` namespace with exactly the values `Unknown` (zero ordinal, default), `Core`, `Supplement`, `Adventure`, and `Setting`.

#### Scenario: BookType has the documented members
- **WHEN** the BookType enum is reflected
- **THEN** it contains exactly those values and `Unknown` is the zero/default value

### Requirement: Books carry a BookType from registration through retrieval
Every `IngestionRecord` SHALL store a `BookType` value. Every Qdrant block point produced from that record SHALL carry the same `book_type` keyword payload field. Retrieval results SHALL surface `BookType` in their metadata.

#### Scenario: Registered book stores BookType in SQLite
- **WHEN** a book is registered with `bookType=Core`
- **THEN** the resulting `IngestionRecord.BookType == Core` and persists across restarts

#### Scenario: Block points carry BookType in their payload
- **WHEN** the registered book is ingested via `/ingest-blocks`
- **THEN** every Qdrant point upserted for it has `payload.book_type == "Core"`

#### Scenario: Retrieval results expose BookType
- **WHEN** a search returns a hit from a Core-typed book
- **THEN** the result's `metadata.bookType` is `Core`

### Requirement: Unset BookType reads as Unknown
Pre-existing rows in `IngestionRecords` and pre-existing points in `dnd_blocks` from before the BookType field existed SHALL remain readable. Where the field is absent or null, callers SHALL see `BookType.Unknown` rather than a thrown exception or a dropped row.

#### Scenario: Pre-existing IngestionRecord rows expose Unknown
- **WHEN** a record was inserted before this field existed
- **THEN** the EF migration has populated the column with `"Unknown"` for that row, so loading it returns `BookType.Unknown`

#### Scenario: Pre-existing Qdrant points expose Unknown
- **WHEN** a Qdrant point lacks the `book_type` payload field
- **THEN** `QdrantPayloadMapper.ToChunkMetadata` returns `ChunkMetadata` with `BookType.Unknown`

### Requirement: Registration parses BookType permissively
The `POST /admin/books/register` endpoint SHALL accept an optional `bookType` form field. The handler SHALL parse it via `Enum.TryParse<BookType>` (case-insensitive). Missing or unparseable values SHALL silently default to `BookType.Unknown` rather than returning HTTP 400.

#### Scenario: Valid value parses
- **WHEN** `bookType=supplement` is included in the multipart body
- **THEN** the resulting record has `BookType == Supplement`

#### Scenario: Missing field defaults to Unknown
- **WHEN** the multipart body has no `bookType` part
- **THEN** the record has `BookType == Unknown` and registration succeeds with HTTP 202

#### Scenario: Invalid value silently defaults to Unknown
- **WHEN** `bookType=garbage` is included
- **THEN** the record has `BookType == Unknown` and registration succeeds

### Requirement: Retrieval accepts an optional bookType filter
Both `GET /retrieval/search` and `GET /admin/retrieval/search` SHALL accept an optional `bookType` query parameter. When present and parseable, the system SHALL apply a Qdrant keyword filter restricting results to that value. When absent or unparseable, no `book_type` filter is applied.

#### Scenario: bookType filter narrows results
- **WHEN** `?q=fireball&bookType=Core` is issued against a corpus with both Core and Supplement books
- **THEN** every returned result has `metadata.bookType == Core`

#### Scenario: Missing bookType returns all
- **WHEN** the same query is issued without `bookType`
- **THEN** mixed-type matches appear (no filter applied)

#### Scenario: Unparseable bookType is ignored
- **WHEN** `bookType=garbage` is passed
- **THEN** the parameter is silently dropped and results are not filtered by book type

#### Scenario: Filter composes with other filters
- **WHEN** `bookType=Adventure&category=Monster` is passed
- **THEN** results match both filters simultaneously
