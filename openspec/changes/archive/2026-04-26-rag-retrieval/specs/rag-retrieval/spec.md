## ADDED Requirements

### Requirement: Retrieval service accepts a query and returns ranked chunks
The system SHALL provide `IRagRetrievalService.SearchAsync(RetrievalQuery query)` returning an ordered `IList<RetrievalResult>` sorted by descending similarity score.

#### Scenario: Query returns top-k results
- **WHEN** `SearchAsync` is called with a non-empty query text and `TopK = 5`
- **THEN** at most 5 `RetrievalResult` objects are returned, ordered by similarity score descending

#### Scenario: Empty collection returns empty list
- **WHEN** Qdrant has no points and `SearchAsync` is called
- **THEN** an empty list is returned without error

### Requirement: Results below score threshold are excluded
The system SHALL discard any search result whose similarity score is below `RetrievalOptions:ScoreThreshold`.

#### Scenario: Low-scoring results are filtered out
- **WHEN** Qdrant returns 5 results but 2 have scores below the configured threshold
- **THEN** `SearchAsync` returns only the 3 results above the threshold

### Requirement: Filters narrow the search to matching metadata
The system SHALL apply optional `RetrievalQuery` filter fields as AND conditions in the Qdrant payload filter.

#### Scenario: Version filter returns only matching edition
- **WHEN** `SearchAsync` is called with `Version = "2024"`
- **THEN** all returned results have `Metadata.Version = "2024"`

#### Scenario: Category filter returns only matching category
- **WHEN** `SearchAsync` is called with `Category = "spell"`
- **THEN** all returned results have `Metadata.Category = ContentCategory.Spell`

#### Scenario: Combined filters are ANDed
- **WHEN** `SearchAsync` is called with both `Version = "2014"` and `Category = "monster"`
- **THEN** all returned results match both conditions

#### Scenario: No filters applied returns results from all metadata
- **WHEN** `SearchAsync` is called with all filter fields null
- **THEN** results may span any version, category, or source book

### Requirement: TopK is capped at a configured maximum
The system SHALL cap `RetrievalQuery.TopK` at `RetrievalOptions:MaxTopK` (default 20) regardless of what the caller requests.

#### Scenario: TopK above maximum is clamped
- **WHEN** `SearchAsync` is called with `TopK = 100`
- **THEN** at most `MaxTopK` results are returned

### Requirement: GET /retrieval/search endpoint exposes retrieval over HTTP
The system SHALL provide a `GET /retrieval/search` endpoint accepting query parameters `q` (required), `version`, `category`, `sourceBook`, `entityName`, and `topK`, returning an array of retrieval results as JSON.

#### Scenario: Valid query returns JSON results
- **WHEN** `GET /retrieval/search?q=fireball&version=2024&category=spell` is called
- **THEN** HTTP 200 is returned with a JSON array of results including `text`, `metadata`, and `score`

#### Scenario: Missing q parameter returns 400
- **WHEN** `GET /retrieval/search` is called without the `q` parameter
- **THEN** HTTP 400 is returned with a descriptive error

### Requirement: GET /admin/retrieval/search provides diagnostic search
The system SHALL provide a `GET /admin/retrieval/search` endpoint (API key required) with the same parameters as the public endpoint plus internal fields (`pointId`, raw `score`) in the response.

#### Scenario: Admin search returns internal diagnostic fields
- **WHEN** `GET /admin/retrieval/search?q=fireball` is called with a valid API key
- **THEN** HTTP 200 is returned with results that include `pointId` and raw `score` fields
