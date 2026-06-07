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

### Requirement: Retrieval surface SHALL expose entity-aware endpoints in addition to block search

The system SHALL expose entity-aware retrieval endpoints alongside the existing `/retrieval/search` block endpoint:

- `GET /retrieval/entities/{id}` — return one entity by ID
- `GET /retrieval/entities/search` — vector search over entities with structured filters
- `GET /admin/retrieval/entities/search` — admin diagnostic with full `fields` block and Qdrant point ID

The existing `/retrieval/search` and `/admin/retrieval/search` block endpoints SHALL continue to operate against `dnd_blocks` exactly as before.

#### Scenario: Block retrieval is unchanged after this change

- **WHEN** `GET /retrieval/search?q=fireball` is called after the entity capability ships
- **THEN** the response and behaviour are identical to before — querying `dnd_blocks`, returning block records ordered by score, with the same filter parameters

#### Scenario: Entity retrieval endpoints are reachable

- **WHEN** any entity retrieval endpoint is called against a populated entity collection
- **THEN** the endpoint responds with entity data, not block data

### Requirement: Entity vector search SHALL apply structured filters server-side

The `/retrieval/entities/search` endpoint SHALL accept the following query parameters and apply them as Qdrant payload filters: `type`, `sourceBook`, `edition`, `bookType`, `settingTag`, `keyword`, `crNumeric_lte`, `crNumeric_gte`, `spellLevel`, `damageType`. Multiple filters SHALL compose with AND semantics. Unknown values SHALL be silently dropped (consistent with the block-search filter behaviour).

#### Scenario: Type filter restricts to a single entity type

- **WHEN** `?q=fire&type=Spell` is called
- **THEN** every result has `type == "Spell"` regardless of vector similarity

#### Scenario: Composing filters narrows results

- **WHEN** `?q=fire&type=Spell&spellLevel=3` is called
- **THEN** every result has both `type == "Spell"` and `spellLevel == 3`

#### Scenario: Unknown type is dropped silently

- **WHEN** `type=NotARealType` is set
- **THEN** the filter is dropped and the search proceeds without it

### Requirement: Entity-by-ID retrieval SHALL return the full structured fields

`GET /retrieval/entities/{id}` SHALL return the full entity record including all envelope fields and the complete `fields` block. The vector-search endpoint SHALL return only the envelope and a snippet (no full `fields`); callers fetch full data by ID when needed.

#### Scenario: Search returns envelopes only; ID lookup returns full fields

- **WHEN** an entity appears in both a `/retrieval/entities/search` result and a subsequent `/retrieval/entities/{id}` lookup
- **THEN** the search result contains the envelope without `fields`; the by-ID response contains the envelope plus the full `fields` block

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

