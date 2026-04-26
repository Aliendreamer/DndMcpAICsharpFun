# rag-retrieval

## Purpose

Defines the requirements for vector similarity search over the Qdrant collection with optional metadata filters, returning ranked relevant chunks for AI consumption.

## Requirements

### Requirement: Public search endpoint accepts a natural-language query
The system SHALL expose `GET /retrieval/search?q=<text>` and return a ranked list of matching chunks with text, metadata, and similarity score.

#### Scenario: Valid query returns results
- **WHEN** `GET /retrieval/search?q=fireball` is called
- **THEN** the system returns HTTP 200 with a JSON array of `RetrievalResult` objects ordered by descending similarity score

#### Scenario: Missing query parameter returns 400
- **WHEN** `GET /retrieval/search` is called without the `q` parameter
- **THEN** the system returns HTTP 400

### Requirement: Results can be filtered by version, category, source book, and entity name
The system SHALL apply Qdrant payload filters when `version`, `category`, `sourceBook`, or `entityName` query parameters are provided.

#### Scenario: Version filter limits results to the specified edition
- **WHEN** `GET /retrieval/search?q=fireball&version=Dnd2024` is called
- **THEN** all returned results have `metadata.version == Dnd2024`

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
