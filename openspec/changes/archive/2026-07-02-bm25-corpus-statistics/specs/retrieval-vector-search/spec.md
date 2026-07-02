## ADDED Requirements

### Requirement: BM25 term statistics are corpus-global and persisted

BM25 IDF and length normalization SHALL derive from persisted corpus-wide statistics — per-term
document frequency, total document count, and average document length — maintained across ingestion,
not from the documents in a single batch. Query-time vectorization SHALL reuse the same persisted
statistics. (COR-15)

#### Scenario: Identical text weights consistently across batches
- **WHEN** the same text is ingested in two different batches
- **THEN** its sparse weights are identical (IDF is not recomputed per batch)

#### Scenario: Single-document query weights by corpus IDF
- **WHEN** a one-term query is vectorized
- **THEN** its IDF comes from the persisted corpus statistics, not a degenerate `df=1, n=1` batch

#### Scenario: Ingestion updates corpus statistics
- **WHEN** a new batch of blocks is ingested
- **THEN** the persisted document-frequency and corpus totals reflect the added documents
