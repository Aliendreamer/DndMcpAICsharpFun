## ADDED Requirements
### Requirement: Retrieval search reranks candidates before returning results
The system SHALL fetch `Reranker:TopK` candidates from Qdrant (default 20) and pass them through the cross-encoder reranker, returning the top `Reranker:TopN` results (default 5) ordered by reranker score. When reranking is disabled, the system SHALL return the top `Reranker:TopN` candidates ordered by Qdrant similarity score. All existing filter parameters (`version`, `category`, `sourceBook`, `entityName`, `bookType`) SHALL apply before candidate selection. The result schema and MCP tool signatures SHALL remain unchanged.

#### Scenario: Reranked results returned when reranker enabled

- **WHEN** `/retrieval/search` is called with reranking enabled
- **THEN** up to TopN results are returned, ordered by cross-encoder relevance score

#### Scenario: Qdrant-ordered results returned when reranker disabled

- **WHEN** `/retrieval/search` is called with `Reranker:Enabled = false`
- **THEN** the top TopN results by Qdrant similarity score are returned without a reranker call

#### Scenario: Filters applied before reranking

- **WHEN** `/retrieval/search` is called with a `category` filter and reranking enabled
- **THEN** only blocks matching the filter are candidates for reranking

#### Scenario: Result count does not exceed TopN

- **WHEN** `/retrieval/search` is called
- **THEN** the response contains at most `Reranker:TopN` results regardless of how many Qdrant candidates were fetched
