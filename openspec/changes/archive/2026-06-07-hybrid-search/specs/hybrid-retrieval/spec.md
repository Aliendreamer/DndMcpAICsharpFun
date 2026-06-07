## ADDED Requirements
### Requirement: BM25 sparse vectors computed at ingestion time
The system SHALL compute a BM25 sparse vector for every ingested block using term-frequency / inverse-document-frequency weighting over the block's text. Terms SHALL be lowercased and split on whitespace and punctuation. IDF SHALL be computed over all blocks in the current ingestion batch. The resulting sparse vector SHALL be stored as `{ indices: int[], values: float[] }` using a vocabulary hash (`abs(term.GetHashCode()) % 30000`) to map terms to indices.

#### Scenario: Sparse vector produced for every block

- **WHEN** a block is processed during ingestion
- **THEN** a BM25 sparse vector with at least one non-zero term is computed and attached to the Qdrant upsert payload

#### Scenario: Common stop words have low IDF weight

- **WHEN** a block contains both rare D&D terms ("Displacer Beast") and common words ("the", "and")
- **THEN** the rare terms have significantly higher BM25 scores than the common words

### Requirement: Qdrant collection configured with sparse vector support
The system SHALL create the `dnd_blocks` collection with a named sparse vector field `"text-sparse"` using `Qdrant.Client.Grpc.SparseVectorConfig`. On startup, if the collection exists without sparse vector support, the system SHALL log a warning and continue with dense-only search until re-ingestion is performed.

#### Scenario: Fresh collection created with sparse support

- **WHEN** the application starts against an empty Qdrant volume
- **THEN** the `dnd_blocks` collection is created with both a dense vector and a `text-sparse` named sparse vector

#### Scenario: Existing dense-only collection detected

- **WHEN** the application starts and `dnd_blocks` exists without sparse vector config
- **THEN** a warning is logged and queries fall back to dense-only search

### Requirement: Hybrid query uses RRF fusion
The system SHALL issue hybrid search queries using Qdrant's `Query` API with Reciprocal Rank Fusion (RRF) combining the dense vector query and the BM25 sparse vector query. The `Qdrant:HybridAlpha` config value (default `0.5`, range 0.0–1.0) SHALL control the relative weight of dense vs. sparse results passed to RRF, where `1.0` means dense only and `0.0` means sparse only.

#### Scenario: Hybrid query issued when collection has sparse support

- **WHEN** a retrieval search is performed and `dnd_blocks` has sparse vector support
- **THEN** the Qdrant query includes both dense and sparse vector components fused via RRF

#### Scenario: Fallback to dense-only when sparse unavailable

- **WHEN** a retrieval search is performed and the collection has no sparse vectors
- **THEN** the search falls back to a standard dense vector query and returns results normally

#### Scenario: Alpha weight respected

- **WHEN** `Qdrant:HybridAlpha` is set to `1.0`
- **THEN** only the dense vector component contributes to result ranking
