# rag-retrieval

## Purpose

Defines the requirements for vector similarity search over the Qdrant blocks collection with optional metadata filters, returning ranked relevant blocks for AI consumption. Retrieval always queries the blocks collection (`Qdrant:BlocksCollectionName`); there is no runtime selector.

## Requirements

### Requirement: Public search endpoint accepts a natural-language query
The system SHALL expose `GET /retrieval/search?q=<text>` and return a ranked list of matching blocks with text, metadata, and similarity score.

#### Scenario: Valid query returns results
- **WHEN** `GET /retrieval/search?q=fireball` is called against a populated blocks collection
- **THEN** the system returns HTTP 200 with a JSON array of `RetrievalResult` objects ordered by descending similarity score

#### Scenario: Missing query parameter returns 400
- **WHEN** `GET /retrieval/search` is called without the `q` parameter
- **THEN** the system returns HTTP 400

#### Scenario: Empty collection returns an empty list
- **WHEN** the same query is issued against an empty `dnd_blocks` collection
- **THEN** the response is HTTP 200 with an empty list, not an error

### Requirement: Results can be filtered by version, category, source book, entity name, and book type
The system SHALL apply Qdrant payload filters when `version`, `category`, `sourceBook`, `entityName`, or `bookType` query parameters are provided. `bookType` accepts case-insensitive values from the `BookType` enum (`Core`, `Supplement`, `Adventure`, `Setting`, `Unknown`); unparseable values are silently dropped.

#### Scenario: BookType filter limits results to a publishing class
- **WHEN** `GET /retrieval/search?q=fireball&bookType=Core` is called
- **THEN** all returned results have `metadata.bookType == Core`

#### Scenario: Filters compose
- **WHEN** `bookType=Adventure&category=Monster` is set
- **THEN** results match both filters simultaneously

#### Scenario: Version filter limits results to the specified edition
- **WHEN** `GET /retrieval/search?q=fireball&version=Edition2024` is called
- **THEN** all returned results have `metadata.version == Edition2024`

#### Scenario: Category filter limits results to the specified content type
- **WHEN** `GET /retrieval/search?q=fireball&category=Spell` is called
- **THEN** all returned results have `metadata.category == Spell`

#### Scenario: Unknown filter values are ignored
- **WHEN** `version` or `category` query parameters contain unrecognised values
- **THEN** those filters are silently dropped and the search proceeds without them

### Requirement: Result count is bounded by topK and a server-side maximum
The system SHALL return at most `topK` results (default 5), capped at `Retrieval:MaxTopK` (default 20).

#### Scenario: Default topK returns at most 5 results
- **WHEN** `GET /retrieval/search?q=fireball` is called without a `topK` parameter
- **THEN** the response contains at most 5 results

#### Scenario: topK is capped at MaxTopK
- **WHEN** `topK` is requested higher than `Retrieval:MaxTopK`
- **THEN** the system returns at most `MaxTopK` results

### Requirement: Results below the score threshold are excluded
The system SHALL exclude any result whose similarity score is below `Retrieval:ScoreThreshold` (default 0.5).

#### Scenario: Low-scoring results are filtered out
- **WHEN** a search returns candidate results with scores below the threshold
- **THEN** those candidates do not appear in the response

### Requirement: An admin diagnostic endpoint returns extended result metadata
The system SHALL expose `GET /admin/retrieval/search` returning `RetrievalDiagnosticResult` which includes the Qdrant point ID in addition to all standard result fields.

#### Scenario: Diagnostic results include point ID
- **WHEN** `GET /admin/retrieval/search?q=fireball` is called with a valid admin key
- **THEN** each result contains a non-empty `pointId` field

### Requirement: Retrieval always queries the blocks collection
The system SHALL route every `/retrieval/search` and `/admin/retrieval/search` query to the Qdrant collection named by `Qdrant:BlocksCollectionName`. There is no runtime collection selector; the legacy `Retrieval:Collection` configuration knob has been removed.

#### Scenario: Search hits the blocks collection
- **WHEN** any retrieval query is issued
- **THEN** the underlying Qdrant search targets `Qdrant:BlocksCollectionName` (default `dnd_blocks`)
