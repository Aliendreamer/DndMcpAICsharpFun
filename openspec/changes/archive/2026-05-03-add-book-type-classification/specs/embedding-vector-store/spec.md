# embedding-vector-store (delta)

## ADDED Requirements

### Requirement: book_type is a keyword payload field on every block point
Every Qdrant point upserted into the blocks collection SHALL carry a `book_type` payload field whose value is the string form of the source record's `BookType` enum. The collection SHALL maintain a keyword index on `book_type` so filtering by it is O(1) lookup.

#### Scenario: Payload write includes book_type
- **WHEN** a block is upserted into Qdrant from a record with `BookType == Adventure`
- **THEN** the point's payload contains `"book_type": "Adventure"`

#### Scenario: Initialiser creates the book_type index
- **WHEN** the application starts and creates the `dnd_blocks` collection
- **THEN** the collection has a keyword payload index on `book_type` alongside the existing keyword indexes

#### Scenario: Pre-existing points lack the field gracefully
- **WHEN** a Qdrant search hits a point inserted before this change (no `book_type` in its payload)
- **THEN** the search succeeds; the result's mapped `ChunkMetadata.BookType` is `Unknown`
