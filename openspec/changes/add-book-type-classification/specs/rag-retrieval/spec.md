# rag-retrieval (delta)

## ADDED Requirements

### Requirement: Search accepts an optional bookType filter
The system SHALL accept an optional `bookType` query parameter on `GET /retrieval/search` and `GET /admin/retrieval/search`. When present and parseable to a `BookType` enum value, the system SHALL apply a Qdrant keyword filter restricting results to points whose `book_type` payload exactly matches. When absent or unparseable, no `book_type` filter SHALL be applied (queries behave exactly as they do today).

#### Scenario: bookType filter narrows results
- **WHEN** `GET /retrieval/search?q=fireball&bookType=Core` is called against a corpus with both Core and Supplement books
- **THEN** every returned result has `metadata.bookType == Core`

#### Scenario: Missing bookType param returns all
- **WHEN** the same query is called without `bookType`
- **THEN** results are not filtered by book type; mixed-type matches appear

#### Scenario: Unparseable bookType is ignored
- **WHEN** `bookType=garbage` is passed
- **THEN** the parameter is silently ignored (no HTTP 400) and results are not filtered by book type

#### Scenario: Filter composes with other filters
- **WHEN** `bookType=Adventure&category=Monster` is called
- **THEN** results match both filters simultaneously (Adventure-book monster blocks)
