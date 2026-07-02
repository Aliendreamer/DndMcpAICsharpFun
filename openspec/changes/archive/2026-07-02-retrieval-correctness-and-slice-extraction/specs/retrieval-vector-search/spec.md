## ADDED Requirements

### Requirement: Sparse term indexing is deterministic across processes

The BM25 sparse-vector term→index mapping SHALL be computed with a hash that is stable across process
lifetimes (e.g. FNV-1a / xxHash over the term's UTF-8 bytes), never `String.GetHashCode()`. The same
term SHALL map to the same index at ingestion time and at query time in a later process. (COR-16,
COR-17)

#### Scenario: A term maps to a fixed index across runs
- **WHEN** a known term is vectorized in two separate processes
- **THEN** it produces the same sparse index (asserted against a golden value)

#### Scenario: Hybrid recall survives a restart
- **WHEN** blocks are ingested, the app restarts, and a keyword query is run
- **THEN** the query-time sparse vector aligns with the stored indices (non-degenerate BM25 recall)

### Requirement: BM25 statistics are corpus-global

Document frequency, corpus size, and average document length used for IDF SHALL be derived from
corpus-wide statistics (persisted or precomputed), not from the documents in a single batch. Query
time SHALL reuse the same global statistics. (COR-15)

#### Scenario: Identical text weights consistently across batches
- **WHEN** the same text is ingested in two different batches
- **THEN** its sparse weights are identical (IDF is not recomputed per batch)

#### Scenario: Single-document query still weights by IDF
- **WHEN** a one-term query is vectorized
- **THEN** IDF comes from the corpus statistics, not a degenerate `df=1,n=1` batch

### Requirement: Tokenizer preserves alphanumeric terms

The BM25 tokenizer SHALL treat alphanumeric characters as token members so numeric and mixed
keyword terms are retained. (COR-14)

#### Scenario: A numeric term survives tokenization
- **WHEN** text containing a numeric keyword (e.g. `2d6`) is tokenized
- **THEN** the numeric term is present in the token stream

### Requirement: Sparse-search primitives live in a feature-neutral module

`SparseVector` and `Bm25Vectorizer` SHALL reside in a shared, feature-neutral module depended upon by
ingestion, vector store, retrieval, and the Qdrant client. No `Features/*` slice SHALL reference
another slice's internal types for sparse search, and `Infrastructure/Qdrant` SHALL NOT depend on a
`Features/*` type. (STR-14, STR-15)

#### Scenario: No cross-slice sparse dependency
- **WHEN** the solution is built
- **THEN** `Features/VectorStore` and `Features/Retrieval` reference the shared kernel, not
  `Features/Ingestion`, for `SparseVector`/`Bm25Vectorizer`

#### Scenario: Infrastructure does not depend upward
- **WHEN** the dependency graph is inspected
- **THEN** `Infrastructure/Qdrant` depends only on the shared kernel / Domain, never on `Features/*`

### Requirement: Retrieval slice has no dead reranking code and factory-based HTTP

The retrieval slice SHALL expose a single reranking implementation (no dead `IReranker.SelectTopN` or
unused `RerankerOptions.TopN`), register reranker services through the retrieval DI extension, obtain
`HttpClient` via `IHttpClientFactory`, log download failures with the exception, and share the
public/diagnostic endpoint scaffolding rather than duplicating it. (SIM-17, SIM-18, SIM-19, STR-17,
NET-08, NET-09, NET-10)

#### Scenario: Reranking has one code path
- **WHEN** results are reranked
- **THEN** the single `RerankingService` path is used (no duplicated top-N logic on the interface)

#### Scenario: Model download uses the factory and surfaces failures
- **WHEN** a model download fails
- **THEN** the client came from `IHttpClientFactory` and the failure is logged with its exception

#### Scenario: Download failure path is tested without a live socket
- **WHEN** the download-failure test runs
- **THEN** it uses an injected failing handler, not a real TCP connection
