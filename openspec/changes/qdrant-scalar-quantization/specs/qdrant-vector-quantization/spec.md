## ADDED Requirements

### Requirement: Dense vectors use configurable scalar int8 quantization

The Qdrant collections SHALL configure scalar int8 quantization for their dense vectors when quantization is enabled. A `QdrantOptions.Quantization` option SHALL control it, defaulting to enabled. New collections SHALL be created with the quantization config; sparse vectors SHALL be unaffected.

#### Scenario: A new collection is created with quantization

- **WHEN** a collection is created and quantization is enabled
- **THEN** its dense vector params include a scalar int8 quantization config (with the configured quantile and `always_ram`)

#### Scenario: Quantization can be disabled by configuration

- **WHEN** `QdrantOptions.Quantization.Enabled` is false
- **THEN** collections are created without a quantization config (current behaviour is preserved)

### Requirement: Quantization is applied in place to existing collections, idempotently

When quantization is enabled and an existing collection has no quantization configured, the system SHALL enable it in place via a collection update (no re-ingestion). When the collection is already quantized, the system SHALL make no change.

#### Scenario: Existing collection is quantized without re-ingestion

- **WHEN** the app starts, quantization is enabled, and an existing collection is not yet quantized
- **THEN** the collection is updated to add scalar quantization, and its vectors and payloads are NOT deleted or re-ingested

#### Scenario: Startup is idempotent

- **WHEN** the app starts and the collection is already quantized
- **THEN** no collection update is issued for quantization

### Requirement: Search preserves recall via rescoring against original vectors

Dense and hybrid searches over a quantized collection SHALL rescore an oversampled candidate set against the original (unquantized) vectors before returning the final results, so that recall is preserved.

#### Scenario: Quantized search returns rescored results

- **WHEN** a search runs against a quantized collection
- **THEN** the query oversamples candidates and rescores them against the original vectors, returning the requested number of results ranked by the rescored similarity

#### Scenario: Recall is validated against the float32 baseline

- **WHEN** the same fixed query set is run before and after enabling quantization
- **THEN** the quantized-with-rescoring results are comparable to the float32 baseline (recall within an accepted tolerance), and the decision to keep quantization is based on that measurement
