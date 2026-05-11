## MODIFIED Requirements

### Requirement: Retrieval search returns hybrid-ranked results
The system SHALL issue hybrid search queries (dense + BM25 sparse, RRF fusion) when the `dnd_blocks` collection has sparse vector support. When sparse support is unavailable the system SHALL fall back to pure dense vector search. All existing filter parameters (`version`, `category`, `sourceBook`, `entityName`, `bookType`) SHALL apply to hybrid queries identically to dense-only queries. The result schema and MCP tool signatures SHALL remain unchanged.

#### Scenario: Hybrid search used when collection supports it
- **WHEN** `/retrieval/search` is called and the collection has sparse vectors
- **THEN** results are ranked by RRF fusion of dense and BM25 scores

#### Scenario: Filters compose with hybrid search
- **WHEN** `/retrieval/search` is called with a `category` filter and sparse vector support is available
- **THEN** results are filtered by category AND ranked by hybrid scores

#### Scenario: Exact name query ranks correctly
- **WHEN** the query is "Sentinel feat" and the Sentinel feat block is in the corpus
- **THEN** the Sentinel feat block appears in the top results

#### Scenario: Dense fallback when sparse unavailable
- **WHEN** `/retrieval/search` is called and the collection has no sparse vectors
- **THEN** results are returned using dense vector similarity only with no error
